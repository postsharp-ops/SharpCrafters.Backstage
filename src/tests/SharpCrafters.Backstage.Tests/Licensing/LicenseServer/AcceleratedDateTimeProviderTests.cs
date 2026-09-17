// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Testing;
using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace SharpCrafters.Backstage.Tests.Licensing.LicenseServer;

/// <summary>
/// Tests the clock that follows the accelerated clock of a license server, which is what lets a load simulation live
/// through weeks of leases in minutes of real time.
/// </summary>
public sealed class AcceleratedDateTimeProviderTests : LicensingTestsBase
{
    private static readonly DateTime _serverTime = new( 2026, 6, 1, 12, 0, 0, DateTimeKind.Utc );

    public AcceleratedDateTimeProviderTests( ITestOutputHelper logger ) : base( logger )
    {
        this.Time.Set( _serverTime, false );
    }

    private LicenseServerSimulator CreateServer()
    {
        this.EnsureServicesInitialized();

        return new LicenseServerSimulator( this.HttpClientFactory, this.Time ) { LicenseKey = "KEY" };
    }

    private AcceleratedDateTimeProvider CreateClock( LicenseServerSimulator server )
        => new( this.HttpClientFactory, server.Url );

    [Fact]
    public async Task ClockAnchorsOnTheInstantReportedByTheServer()
    {
        var server = this.CreateServer();
        server.Acceleration = 1;

        var clock = this.CreateClock( server );
        await clock.SyncAsync();

        // The acceleration is one, so the virtual clock advances at real speed and the reading is the server's own,
        // give or take the few microseconds the test itself takes.
        Assert.InRange( clock.UtcNow, _serverTime, _serverTime.AddSeconds( 5 ) );
    }

    [Fact]
    public async Task AccelerationIsReadFromTheServer()
    {
        var server = this.CreateServer();
        server.Acceleration = 1440;

        var clock = this.CreateClock( server );
        await clock.SyncAsync();

        Assert.Equal( 1440m, clock.Acceleration );
    }

    /// <summary>
    /// Tests that real time is multiplied by the acceleration factor. At 1440, which is the default of a real server,
    /// one real minute is one virtual day.
    /// </summary>
    [Fact]
    public async Task VirtualTimeAdvancesFasterThanRealTime()
    {
        var server = this.CreateServer();
        server.Acceleration = 100000;

        var clock = this.CreateClock( server );
        await clock.SyncAsync();

        var first = clock.UtcNow;
        await Task.Delay( TimeSpan.FromMilliseconds( 50 ) );
        var second = clock.UtcNow;

        // 50 ms of real time is 5000 s of virtual time at this factor. The assertion is generous at both ends,
        // because a test agent can be slow, but it cannot pass at an acceleration of one.
        Assert.True(
            second - first > TimeSpan.FromSeconds( 100 ),
            $"The virtual clock advanced by {second - first}, which is not faster than real time." );
    }

    /// <summary>
    /// Tests that the clock reads real time before it has been synchronized, rather than an instant of its own. A
    /// harness that forgets to synchronize then runs slowly instead of behaving strangely.
    /// </summary>
    [Fact]
    public void ClockReadsRealTimeBeforeSynchronization()
    {
        var server = this.CreateServer();
        var clock = this.CreateClock( server );

        Assert.Equal( 1m, clock.Acceleration );
        Assert.InRange( clock.UtcNow, DateTime.UtcNow.AddSeconds( -5 ), DateTime.UtcNow.AddSeconds( 5 ) );
    }

    [Fact]
    public async Task SynchronizingAgainReanchorsTheClock()
    {
        var server = this.CreateServer();
        server.Acceleration = 1;

        var clock = this.CreateClock( server );
        await clock.SyncAsync();

        this.Time.Set( _serverTime.AddDays( 10 ), false );
        await clock.SyncAsync();

        Assert.Equal( 2, clock.SyncCount );
        Assert.InRange( clock.UtcNow, _serverTime.AddDays( 10 ), _serverTime.AddDays( 10 ).AddSeconds( 5 ) );
    }

    /// <summary>
    /// Tests that waiting for an instant that has already passed returns at once, rather than computing a negative
    /// delay.
    /// </summary>
    [Fact]
    public async Task WaitingForAPastInstantReturnsImmediately()
    {
        var server = this.CreateServer();
        var clock = this.CreateClock( server );
        await clock.SyncAsync();

        await clock.WaitUntilAsync( _serverTime.AddDays( -1 ) );
    }

    /// <summary>
    /// Tests that the real duration of a wait is the virtual duration divided by the acceleration factor. Without the
    /// division a harness would wait for the virtual duration, which at a realistic factor is weeks.
    /// </summary>
    [Fact]
    public async Task WaitingIsDividedByTheAcceleration()
    {
        var server = this.CreateServer();
        server.Acceleration = 100000;

        var clock = this.CreateClock( server );
        await clock.SyncAsync();

        var startedAt = DateTime.UtcNow;

        // Ten virtual minutes at this factor is six milliseconds of real time.
        await clock.WaitUntilAsync( clock.UtcNow.AddMinutes( 10 ) );

        Assert.True(
            DateTime.UtcNow - startedAt < TimeSpan.FromSeconds( 10 ),
            "The wait took the virtual duration instead of the real one." );
    }

    /// <summary>
    /// Tests that waiting before the first synchronization does not throw. The original divided by an acceleration
    /// factor initialized to zero, so this call produced an infinite delay.
    /// </summary>
    [Fact]
    public async Task WaitingBeforeSynchronizationDoesNotThrow()
    {
        var server = this.CreateServer();
        var clock = this.CreateClock( server );

        await clock.WaitUntilAsync( DateTime.UtcNow.AddMilliseconds( 1 ) );
    }

    /// <summary>
    /// Tests that a base URL without a trailing slash reaches the time endpoint. The original concatenated the two
    /// without a separator here while trimming for the lease endpoint, so such a URL broke the clock alone.
    /// </summary>
    [Theory]
    [InlineData( "https://license.test" )]
    [InlineData( "https://license.test/" )]
    public async Task BaseUrlIsNormalized( string url )
    {
        var server = this.CreateServer();
        server.Acceleration = 7;

        var clock = new AcceleratedDateTimeProvider( this.HttpClientFactory, url );
        await clock.SyncAsync();

        Assert.Equal( 7m, clock.Acceleration );
    }

    /// <summary>
    /// Tests that an acceleration of zero, which a server uses to mean that acceleration is disabled, is read as one
    /// rather than as a stopped clock.
    /// </summary>
    [Fact]
    public async Task ZeroAccelerationIsReadAsRealSpeed()
    {
        var server = this.CreateServer();
        server.Acceleration = 0;

        var clock = this.CreateClock( server );
        await clock.SyncAsync();

        Assert.Equal( 1m, clock.Acceleration );
    }

    [Theory]
    [InlineData( "" )]
    [InlineData( "not-a-time" )]
    [InlineData( "2026-06-01T12:00:00Z" )]
    [InlineData( "2026-06-01T12:00:00Z;not-a-number" )]
    [InlineData( "not-a-time;1440" )]
    [InlineData( "2026-06-01T12:00:00Z;1440;extra" )]
    public async Task MalformedAnswerIsReported( string body )
    {
        var server = this.CreateServer();
        server.ServesTime = false;

        this.HttpClientFactory.InsertHook(
            request => request.RequestUri!.AbsolutePath.EndsWith( LicenseServerSimulator.TimePath, StringComparison.OrdinalIgnoreCase ),
            ( _, _ ) => Task.FromResult( new HttpResponseMessage( HttpStatusCode.OK ) { Content = new StringContent( body ) } ) );

        var clock = this.CreateClock( server );

        await Assert.ThrowsAsync<InvalidOperationException>( () => clock.SyncAsync() );
    }

    /// <summary>
    /// Tests that a server which does not answer the time endpoint at all surfaces as a failure of the synchronization
    /// and not as a clock that silently reads something wrong.
    /// </summary>
    [Fact]
    public async Task ServerWithoutTimeEndpointIsReported()
    {
        var server = this.CreateServer();
        server.ServesTime = false;

        var clock = this.CreateClock( server );

        await Assert.ThrowsAsync<InvalidOperationException>( () => clock.SyncAsync() );
    }
}
