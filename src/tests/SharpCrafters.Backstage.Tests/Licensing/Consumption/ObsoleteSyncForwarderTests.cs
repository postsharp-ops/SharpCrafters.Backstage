// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Configuration;
using SharpCrafters.Backstage.Extensibility;
using SharpCrafters.Backstage.Licensing.Consumption;
using SharpCrafters.Backstage.Licensing.Consumption.Sources;
using SharpCrafters.Backstage.Licensing.Licenses;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace SharpCrafters.Backstage.Tests.Licensing.Consumption;

/// <summary>
/// Tests the synchronous members that the licensing services keep for the callers that have not moved to the
/// asynchronous ones.
/// </summary>
/// <remarks>
/// These members are obsolete and block, which is exactly why they are worth a test: they are the only place in the
/// licensing code that waits for a task, and getting that wrong deadlocks a user interface thread rather than merely
/// running slowly.
/// </remarks>
#pragma warning disable CS0618 // The obsolete members are the subject of these tests.
public sealed class ObsoleteSyncForwarderTests : LicensingTestsBase
{
    public ObsoleteSyncForwarderTests( ITestOutputHelper logger ) : base( logger )
    {
        this.UserDeviceDetection.IsInteractiveDevice = true;
    }

    private ILicenseConsumptionService ConsumptionService
        => this.ServiceProvider.GetRequiredBackstageService<ILicenseConsumptionService>();

    /// <summary>
    /// A synchronization context that runs every continuation on a single dedicated thread, which is what a user
    /// interface does. A naive <c>.Result</c> on a task whose continuation is posted here deadlocks, because the
    /// thread is blocked waiting for the very continuation it would have to run.
    /// </summary>
    private sealed class SingleThreadSynchronizationContext : SynchronizationContext, IDisposable
    {
        private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue = new();
        private readonly Thread _thread;

        public SingleThreadSynchronizationContext()
        {
            this._thread = new Thread(
                () =>
                {
                    SetSynchronizationContext( this );

                    foreach ( var item in this._queue.GetConsumingEnumerable() )
                    {
                        item.Callback( item.State );
                    }
                } ) { IsBackground = true };

            this._thread.Start();
        }

        public override void Post( SendOrPostCallback d, object? state ) => this._queue.Add( (d, state) );

        /// <summary>
        /// Runs an action on the thread of this context and returns what it produced, or rethrows what it raised.
        /// </summary>
        public T Run<T>( Func<T> action, TimeSpan timeout )
        {
            T result = default!;
            Exception? exception = null;

            using var completed = new ManualResetEventSlim();

            this.Post(
                _ =>
                {
                    try
                    {
                        result = action();
                    }
                    catch ( Exception e )
                    {
                        exception = e;
                    }
                    finally
                    {
                        // ReSharper disable once AccessToDisposedClosure
                        completed.Set();
                    }
                },
                null );

            if ( !completed.Wait( timeout ) )
            {
                throw new TimeoutException(
                    $"The operation did not complete within {timeout.TotalSeconds} seconds on a thread that has a synchronization context, which means it deadlocked." );
            }

            if ( exception != null )
            {
                throw exception;
            }

            return result;
        }

        public void Dispose() => this._queue.CompleteAdding();
    }

    /// <summary>
    /// Tests that the synchronous overload produces the same consumer as the asynchronous one.
    /// </summary>
    [Fact]
    public async Task CreateConsumerMatchesCreateConsumerAsync()
    {
        var licensingConfiguration = this.ConfigurationManager!.Get<SharpCrafters.Backstage.Licensing.LicensingConfiguration>();
        this.ConfigurationManager!.Set( licensingConfiguration with { LegacyLicense = LicenseKeyProvider.MetalamaProfessionalBusiness } );

        var expected = (await this.ConsumptionService.CreateConsumerAsync()).TryConsume( LicenseRequirement.Any );
        var actual = this.ConsumptionService.CreateConsumer().TryConsume( LicenseRequirement.Any );

        Assert.True( expected );
        Assert.Equal( expected, actual );
    }

    /// <summary>
    /// Tests that the synchronous overload agrees with the asynchronous one when there is no licence, so that the
    /// two are equivalent on the failure path too.
    /// </summary>
    [Fact]
    public async Task CreateConsumerMatchesCreateConsumerAsyncWithoutLicense()
    {
        var expected = (await this.ConsumptionService.CreateConsumerAsync()).TryConsume( LicenseRequirement.Any );
        var actual = this.ConsumptionService.CreateConsumer().TryConsume( LicenseRequirement.Any );

        Assert.False( expected );
        Assert.Equal( expected, actual );
    }

    /// <summary>
    /// Tests that registering a licence through the older member registers the same licence as the newer one. A
    /// product that has not yet moved to the new member must go on working exactly as it did, or upgrading this
    /// package would break it.
    /// </summary>
    [Fact]
    public async Task RegisterLicenseMatchesRegisterLicenseAsync()
    {
        var asyncResult = await this.LicenseRegistrationService.RegisterLicenseAsync( LicenseKeyProvider.MetalamaProfessionalBusiness );
        this.LicenseRegistrationService.RemoveLicenses();
        var syncResult = this.LicenseRegistrationService.RegisterLicense( LicenseKeyProvider.MetalamaProfessionalBusiness );

        Assert.True( asyncResult.IsSuccess );
        Assert.Equal( asyncResult.IsSuccess, syncResult.IsSuccess );
        Assert.Equal( asyncResult.RegisteredLicense?.LicenseString, syncResult.RegisteredLicense?.LicenseString );
    }

    /// <summary>
    /// Tests that a failure surfaces through the synchronous overload exactly as it does through the asynchronous
    /// one, rather than as a success or as an exception.
    /// </summary>
    [Fact]
    public async Task RegisterLicenseReportsTheSameFailure()
    {
        var asyncResult = await this.LicenseRegistrationService.RegisterLicenseAsync( LicenseKeyProvider.InvalidLicenseKey );
        var syncResult = this.LicenseRegistrationService.RegisterLicense( LicenseKeyProvider.InvalidLicenseKey );

        Assert.False( asyncResult.IsSuccess );
        Assert.False( syncResult.IsSuccess );
        Assert.Equal( asyncResult.ErrorMessage, syncResult.ErrorMessage );
    }

    /// <summary>
    /// Tests that reading a licence through the older members answers what the newer ones answer, so that a product
    /// which has not moved yet shows its users the same thing it showed them before the upgrade.
    /// </summary>
    [Fact]
    public async Task ValidateAndParseMatchTheirAsyncCounterparts()
    {
        var licenseKey = LicenseKeyProvider.MetalamaProfessionalBusiness;

        Assert.Equal(
            (await this.LicenseRegistrationService.ValidateLicenseKeyAsync( licenseKey )).IsSuccess,
            this.LicenseRegistrationService.ValidateLicenseKey( licenseKey ).IsSuccess );

        Assert.Equal(
            (await this.LicenseRegistrationService.ResolveLicenseAsync( licenseKey )).IsSuccess,
            this.LicenseRegistrationService.ParseLicenseKey( licenseKey ).IsSuccess );
    }

    /// <summary>
    /// Tests that the synchronous overloads do not deadlock when they are called from a thread that has a
    /// synchronization context, which is the shape of a user interface thread and the reason these overloads run
    /// their work on the thread pool rather than awaiting it in place.
    /// </summary>
    [Theory]
    [InlineData( "CreateConsumer" )]
    [InlineData( "RegisterLicense" )]
    [InlineData( "ValidateLicenseKey" )]
    [InlineData( "ParseLicenseKey" )]
    public void SynchronousOverloadDoesNotDeadlockOnAThreadWithASynchronizationContext( string member )
    {
        this.EnsureServicesInitialized();

        using var context = new SingleThreadSynchronizationContext();

        var succeeded = context.Run<bool>(
            () => member switch
            {
                "CreateConsumer" => this.ConsumptionService.CreateConsumer() != null,
                "RegisterLicense" => this.LicenseRegistrationService.RegisterLicense( LicenseKeyProvider.MetalamaProfessionalBusiness ).IsSuccess,
                "ValidateLicenseKey" => this.LicenseRegistrationService.ValidateLicenseKey( LicenseKeyProvider.MetalamaProfessionalBusiness ).IsSuccess,
                "ParseLicenseKey" => this.LicenseRegistrationService.ParseLicenseKey( LicenseKeyProvider.MetalamaProfessionalBusiness ).IsSuccess,
                _ => throw new ArgumentOutOfRangeException( nameof(member) )
            },
            TimeSpan.FromSeconds( 30 ) );

        Assert.True( succeeded );
    }

    /// <summary>
    /// A licence source that fails, standing for a license server that raises rather than returning a message.
    /// </summary>
    private sealed class ThrowingLicenseSource : ILicenseSource
    {
        public string Description => "throwing license source";

        public LicenseSourceKind Kind => LicenseSourceKind.Test;

        public LicenseSourcePriority Priority => LicenseSourcePriority.UserProfile;

        public bool SupportsRegistration => false;

        public IEnumerable<ILicense> GetLicenses( Action<LicensingMessage> reportMessage )
        {
            throw new InvalidOperationException( "The license source failed." );

#pragma warning disable CS0162 // The yield is unreachable but makes the method an iterator.
            yield break;
#pragma warning restore CS0162
        }

        event Action? ILicenseSource.Changed { add { } remove { } }
    }

    /// <summary>
    /// Tests that an exception raised by the asynchronous operation reaches the caller of the synchronous overload as
    /// itself, and not wrapped in an <see cref="AggregateException"/> whose message names nothing. This is why the
    /// overload ends in <c>GetAwaiter().GetResult()</c> rather than in <c>Wait()</c> or <c>.Result</c>.
    /// </summary>
    [Fact]
    public void ExceptionIsNotWrappedInAnAggregateException()
    {
        var service = new LicenseConsumptionService( this.ServiceProvider, [new ThrowingLicenseSource()] );

        var exception = Record.Exception( () => service.CreateConsumer() );

        Assert.IsType<InvalidOperationException>( exception );
        Assert.Equal( "The license source failed.", exception.Message );
    }
}
#pragma warning restore CS0618
