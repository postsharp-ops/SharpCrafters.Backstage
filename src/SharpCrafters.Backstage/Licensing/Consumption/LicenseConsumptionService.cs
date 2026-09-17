// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Diagnostics;
using SharpCrafters.Backstage.Licensing.Consumption.Sources;
using SharpCrafters.Backstage.Licensing.Licenses;
using SharpCrafters.Backstage.Licensing.Registration;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SharpCrafters.Backstage.Licensing.Consumption;

/// <inheritdoc />
internal sealed class LicenseConsumptionService : ILicenseConsumptionService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IReadOnlyList<ILicenseSource> _sources;
    private readonly ILogger _logger;

    public LicenseConsumptionService( IServiceProvider serviceProvider, IReadOnlyList<ILicenseSource> licenseSources )
    {
        this._serviceProvider = serviceProvider;
        this._sources = licenseSources;
        this._logger = this._serviceProvider.GetLoggerFactory().Licensing();

        foreach ( var source in this._sources )
        {
            source.Changed += this.OnSourceChanged;
        }
    }

    private void OnSourceChanged()
    {
        this.Changed?.Invoke();
    }

    public ValueTask<ILicenseConsumer> CreateConsumerAsync(
        LicenseConsumptionOptions? options = null,
        Action<LicensingMessage>? reportMessage = null,
        CancellationToken cancellationToken = default )
    {
        options ??= LicenseConsumptionOptions.Default;

        var sources = new List<ILicenseSource>( this._sources.Count + 1 );

        sources.AddRange( this._sources.Where( s => (s.Kind & options.IgnoredLicenseSources) == 0 ) );

        if ( !string.IsNullOrEmpty( options.ProjectLicenseKey ) )
        {
            // ReSharper disable once RedundantSuppressNullableWarningExpression
            sources.Add( new ExplicitLicenseSource( options.ProjectLicenseKey!, LicenseSourceKind.Project, this._serviceProvider ) );
        }

        return this.CreateConsumerAsync( options, sources, reportMessage, cancellationToken );
    }

    [Obsolete( "Use CreateConsumerAsync." )]
    public ILicenseConsumer CreateConsumer( LicenseConsumptionOptions? options = null, Action<LicensingMessage>? reportMessage = null )

        // Task.Run puts the continuation on a thread-pool thread, where SynchronizationContext.Current is null, so it
        // cannot be posted back to the thread that is blocked here, which is what deadlocks a user interface thread on
        // .NET Framework. GetResult rethrows the original exception, whereas Wait would wrap it in an
        // AggregateException whose message does not name the failure.
        => Task.Run( () => this.CreateConsumerAsync( options, reportMessage, CancellationToken.None ).AsTask() )
            .GetAwaiter()
            .GetResult();

    private async ValueTask<ILicenseConsumer> CreateConsumerAsync(
        LicenseConsumptionOptions options,
        IEnumerable<ILicenseSource> licenseSources,
        Action<LicensingMessage>? reportMessage,
        CancellationToken cancellationToken )
    {
        var validLicenses = ImmutableArray.CreateBuilder<(ILicense License, LicenseConsumptionProperties Properties)>();
        var deferredLicenses = new List<(ILicense License, ILicenseSource Source)>();

        // The sources are drained in the order of their priority, and each yields its licences in its own order, so a
        // registered license key is always considered before a lease is acquired from a license server.
        foreach ( var source in licenseSources.OrderBy( s => s.Priority ) )
        {
            await foreach ( var license in source.GetLicensesAsync( ReportMessage, cancellationToken ).WithCancellation( cancellationToken ) )
            {
                // A licence leased from a license server costs a request and, above all, a seat, so it is examined
                // only once every other licence has failed. Without this, a build that a registered license key
                // already licenses would take a seat from the pool of the team on every run.
                if ( license is LeasedLicense )
                {
                    deferredLicenses.Add( (license, source) );

                    continue;
                }

                var consumptionResult = await license.GetConsumptionPropertiesAsync( options, cancellationToken );

                if ( !consumptionResult.IsSuccess )
                {
                    await ReportUnusableLicenseAsync( license, source, consumptionResult.ErrorMessage! );

                    continue;
                }

                validLicenses.Add( (license, consumptionResult.Properties) );
            }
        }

        // The licences that are acquired on demand are handed to the consumer unresolved. It acquires them the first
        // time a requirement is not satisfied by anything else, so that a seat is taken for a licence that is really
        // used rather than for one that merely might have been.
        return new LicenseConsumer(
            this._serviceProvider,
            validLicenses.ToImmutableArray(),
            options,
            deferredLicenses.Count == 0 ? null : AcquireOnDemandLicensesAsync );

        async ValueTask<ImmutableArray<(ILicense License, LicenseConsumptionProperties Properties)>> AcquireOnDemandLicensesAsync(
            Action<LicensingMessage>? consumerReportMessage,
            CancellationToken onDemandCancellationToken )
        {
            var acquiredLicenses = ImmutableArray.CreateBuilder<(ILicense License, LicenseConsumptionProperties Properties)>();

            foreach ( var deferred in deferredLicenses )
            {
                var consumptionResult = await deferred.License.GetConsumptionPropertiesAsync( options, onDemandCancellationToken );

                if ( consumptionResult.IsSuccess )
                {
                    acquiredLicenses.Add( (deferred.License, consumptionResult.Properties) );
                }
                else
                {
                    // The message goes to the requirement that triggered the acquisition, so that it reaches the
                    // compilation that needed the licence rather than the creation of the consumer.
                    await this.ReportUnusableLicenseAsync(
                        deferred.License,
                        deferred.Source,
                        consumptionResult.ErrorMessage!,
                        consumerReportMessage,
                        onDemandCancellationToken );
                }
            }

            return acquiredLicenses.ToImmutable();
        }

        void ReportMessage( LicensingMessage message )
        {
            reportMessage?.Invoke( message );
            this._logger.Warning?.Log( message.Text );
        }

        Task ReportUnusableLicenseAsync( ILicense license, ILicenseSource source, string errorMessage )
            => this.ReportUnusableLicenseAsync( license, source, errorMessage, reportMessage, cancellationToken );
    }

    /// <summary>
    /// Reports that a licence is present but cannot be used, naming it as well as the licence itself allows.
    /// </summary>
    private async Task ReportUnusableLicenseAsync(
        ILicense license,
        ILicenseSource source,
        string errorMessage,
        Action<LicensingMessage>? reportMessage,
        CancellationToken cancellationToken )
    {
        LicenseRegistrationProperties? registrationProperties = null;

        if ( source.SupportsRegistration )
        {
            var registrationResult = await license.GetRegistrationPropertiesAsync( cancellationToken );
            registrationProperties = registrationResult.Properties;
        }

        var message =
            $"Cannot use the license '{registrationProperties?.LicenseId?.ToString( CultureInfo.InvariantCulture ) ?? registrationProperties?.Description}': {errorMessage}"
                .TrimEnd( '.' ) + ".";

        if ( source.GetType() != typeof(UserProfileLicenseSource) )
        {
            message += $" The license key originates from {source.Description}.";
        }

        reportMessage?.Invoke( new LicensingMessage( message ) );
        this._logger.Warning?.Log( message );
    }

    public event Action? Changed;
}
