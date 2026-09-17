// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Licensing.LicenseServer;
using SharpCrafters.Backstage.Testing;
using System;
using System.Collections.Immutable;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace SharpCrafters.Backstage.Tests.Licensing.LicenseServer;

/// <summary>
/// Tests the license server simulator itself.
/// </summary>
/// <remarks>
/// The simulator is the instrument that every other license server test relies on, so the rules it reproduces — above
/// all the seat accounting, which is the part a reader is most likely to assume rather than check — are pinned here.
/// A simulator that is wrong in the same direction as the client would let a defect pass unnoticed.
/// </remarks>
public sealed class LicenseServerSimulatorTests : LicensingTestsBase
{
    private static readonly DateTime _start = new( 2026, 6, 1, 12, 0, 0, DateTimeKind.Utc );

    public LicenseServerSimulatorTests( ITestOutputHelper logger ) : base( logger )
    {
        this.Time.Set( _start, false );
    }

    private LicenseServerSimulator CreateServer( string url = LicenseServerSimulator.DefaultUrl )
    {
        this.EnsureServicesInitialized();

        return new LicenseServerSimulator( this.HttpClientFactory, this.Time, url ) { LicenseKey = "KEY" };
    }

    private async Task<HttpResponseMessage> RequestLeaseAsync(
        LicenseServerSimulator server,
        string user = "alice",
        string machine = "DESKTOP-1a2b",
        string product = "MetalamaProfessional" )
    {
        using var client = this.HttpClientFactory.Create();

        return await client.GetAsync( $"{server.LeaseUrl}?user={user}&machine={machine}&version=1.0&product={product}" );
    }

    private async Task<string> GetLeaseBodyAsync( LicenseServerSimulator server, string user = "alice", string machine = "DESKTOP-1a2b" )
    {
        using var response = await this.RequestLeaseAsync( server, user, machine );
        Assert.Equal( HttpStatusCode.OK, response.StatusCode );

        return await response.Content.ReadAsStringAsync();
    }

    [Fact]
    public async Task LeaseBodyParsesBackIntoALease()
    {
        var server = this.CreateServer();

        var body = await this.GetLeaseBodyAsync( server );
        this.Logger.WriteLine( body );

        Assert.True( LicenseLease.TryDeserialize( body, this.Time.UtcNow, out var lease ) );
        Assert.Equal( "KEY", lease.LicenseKey );
        Assert.Equal( _start, lease.StartTime );
        Assert.Equal( _start.AddDays( 3 ), lease.EndTime );
        Assert.Equal( _start.AddDays( 2 ), lease.RenewTime );
    }

    /// <summary>
    /// Tests that the request is recorded with its arguments parsed, which is what lets a test assert on values rather
    /// than on a URL.
    /// </summary>
    [Fact]
    public async Task RequestArgumentsAreRecorded()
    {
        var server = this.CreateServer();

        await this.GetLeaseBodyAsync( server, "alice", "DESKTOP-1a2b" );

        var request = Assert.Single( server.Requests );
        Assert.Equal( "alice", request.User );
        Assert.Equal( "DESKTOP-1a2b", request.Machine );
        Assert.Equal( "DESKTOP", request.MachineName );
        Assert.Equal( "1a2b", request.MachineHash );
        Assert.Equal( "1.0", request.Version );
        Assert.Equal( "MetalamaProfessional", request.Product );
        Assert.Equal( _start, request.ReceivedAt );
    }

    [Fact]
    public async Task TwoServersAreRoutedIndependently()
    {
        var primary = this.CreateServer( "https://primary.license.test" );
        var secondary = this.CreateServer( "https://secondary.license.test" );
        primary.LicenseKey = "PRIMARY";
        secondary.LicenseKey = "SECONDARY";

        Assert.Contains( "PRIMARY", await this.GetLeaseBodyAsync( primary ), StringComparison.Ordinal );
        Assert.Contains( "SECONDARY", await this.GetLeaseBodyAsync( secondary ), StringComparison.Ordinal );

        primary.AssertContacted();
        secondary.AssertContacted();
    }

    /// <summary>
    /// Tests the clock endpoint, which is what lets a load simulation run on the accelerated clock of the server.
    /// </summary>
    [Fact]
    public async Task TimeEndpointReportsTheClockAndTheAcceleration()
    {
        var server = this.CreateServer();
        server.Acceleration = 1440;

        using var client = this.HttpClientFactory.Create();
        var body = await client.GetStringAsync( server.TimeUrl );

        Assert.Equal( "2026-06-01T12:00:00Z;1440", body );
    }

    [Fact]
    public async Task TimeEndpointFollowsTheClockOfTheTest()
    {
        var server = this.CreateServer();
        this.Time.AddTime( TimeSpan.FromDays( 1 ) );

        using var client = this.HttpClientFactory.Create();
        var body = await client.GetStringAsync( server.TimeUrl );

        Assert.StartsWith( "2026-06-02T12:00:00Z;", body, StringComparison.Ordinal );
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Seat accounting.
    // ---------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Tests that one user on two machines occupies one seat, because a real server allows two machines per user.
    /// </summary>
    [Fact]
    public async Task OneUserOnTwoMachinesOccupiesOneSeat()
    {
        var server = this.CreateServer();
        server.MaxSeats = 1;

        await this.GetLeaseBodyAsync( server, "alice", "DESKTOP-1" );
        await this.GetLeaseBodyAsync( server, "alice", "LAPTOP-2" );

        Assert.Equal( 1, server.OccupiedSeatCount );
    }

    /// <summary>
    /// Tests the rule of a real server, which is a modulo and not a comparison: a user who holds an odd number of
    /// machines gets the next one without a capacity check, even when every seat is taken.
    /// </summary>
    [Fact]
    public async Task ThirdMachineOfAUserIsGrantedWithoutCapacity()
    {
        var server = this.CreateServer();
        server.MaxSeats = 1;

        await this.GetLeaseBodyAsync( server, "alice", "DESKTOP-1" );
        await this.GetLeaseBodyAsync( server, "alice", "LAPTOP-2" );

        // Two machines is a whole seat, so the third takes a second seat and there is none.
        using ( var denied = await this.RequestLeaseAsync( server, "alice", "TABLET-3" ) )
        {
            Assert.Equal( HttpStatusCode.Forbidden, denied.StatusCode );
        }

        // With three machines held, the fourth is free, because three is not a multiple of two.
        server.MaxSeats = 2;
        await this.GetLeaseBodyAsync( server, "alice", "TABLET-3" );
        server.MaxSeats = 1;
        await this.GetLeaseBodyAsync( server, "alice", "PHONE-4" );

        Assert.Equal( 2, server.OccupiedSeatCount );
    }

    [Fact]
    public async Task SecondUserTakesASecondSeat()
    {
        var server = this.CreateServer();
        server.MaxSeats = 2;

        await this.GetLeaseBodyAsync( server, "alice", "DESKTOP-1" );
        await this.GetLeaseBodyAsync( server, "bob", "DESKTOP-2" );

        Assert.Equal( 2, server.OccupiedSeatCount );
    }

    [Fact]
    public async Task ExhaustedSeatsAreDeniedWithAnExplanation()
    {
        var server = this.CreateServer();
        server.MaxSeats = 1;

        await this.GetLeaseBodyAsync( server, "alice", "DESKTOP-1" );

        using var denied = await this.RequestLeaseAsync( server, "bob", "DESKTOP-2" );

        Assert.Equal( HttpStatusCode.Forbidden, denied.StatusCode );
        Assert.Contains( "No license with free capacity", await denied.Content.ReadAsStringAsync(), StringComparison.Ordinal );
    }

    [Fact]
    public async Task DenialMessageIsServedVerbatim()
    {
        var server = this.CreateServer();
        server.MaxSeats = 1;
        server.DenialMessage = "Contact your administrator: your team has no seat left.";

        await this.GetLeaseBodyAsync( server, "alice", "DESKTOP-1" );

        using var denied = await this.RequestLeaseAsync( server, "bob", "DESKTOP-2" );

        Assert.Equal( server.DenialMessage, await denied.Content.ReadAsStringAsync() );
    }

    /// <summary>
    /// Tests that a seat is released when the lease that holds it ends, which is the only way a real server reclaims
    /// one: there is no expiry job, the lease is simply no longer counted.
    /// </summary>
    [Fact]
    public async Task ExpiredLeaseReleasesItsSeat()
    {
        var server = this.CreateServer();
        server.MaxSeats = 1;

        await this.GetLeaseBodyAsync( server, "alice", "DESKTOP-1" );

        using ( var denied = await this.RequestLeaseAsync( server, "bob", "DESKTOP-2" ) )
        {
            Assert.Equal( HttpStatusCode.Forbidden, denied.StatusCode );
        }

        this.Time.AddTime( TimeSpan.FromDays( 3 ) );

        await this.GetLeaseBodyAsync( server, "bob", "DESKTOP-2" );

        Assert.Equal( 1, server.OccupiedSeatCount );
    }

    [Fact]
    public async Task RenewalDoesNotTakeASecondSeat()
    {
        var server = this.CreateServer();
        server.MaxSeats = 1;

        await this.GetLeaseBodyAsync( server, "alice", "DESKTOP-1" );
        this.Time.AddTime( TimeSpan.FromDays( 2 ) );
        await this.GetLeaseBodyAsync( server, "alice", "DESKTOP-1" );

        Assert.Equal( 1, server.OccupiedSeatCount );
    }

    /// <summary>
    /// Tests that a build server gets a lease without consuming a seat, so that its builds cannot exhaust the seats of
    /// the developers, and that the machine name is matched after its hash suffix has been stripped.
    /// </summary>
    [Fact]
    public async Task BuildServerConsumesNoSeat()
    {
        var server = this.CreateServer();
        server.MaxSeats = 1;
        server.BuildServerMachines = ImmutableHashSet.Create( StringComparer.OrdinalIgnoreCase, "BUILDSERVER" );

        await this.GetLeaseBodyAsync( server, "ci", "BUILDSERVER-99ff" );
        await this.GetLeaseBodyAsync( server, "ci2", "BUILDSERVER-aa11" );

        Assert.Equal( 0, server.OccupiedSeatCount );

        // The single seat is still free for a developer.
        await this.GetLeaseBodyAsync( server, "alice", "DESKTOP-1" );
        Assert.Equal( 1, server.OccupiedSeatCount );
    }

    /// <summary>
    /// Tests the grace period, which lets a real server lease beyond its capacity for a while rather than break a
    /// build the moment a team grows.
    /// </summary>
    [Fact]
    public async Task GracePeriodAllowsSeatsBeyondCapacity()
    {
        var server = this.CreateServer();
        server.MaxSeats = 2;
        server.GracePercent = 50;
        server.GraceDays = 30;

        await this.GetLeaseBodyAsync( server, "alice", "DESKTOP-1" );
        await this.GetLeaseBodyAsync( server, "bob", "DESKTOP-2" );

        // ceil( 2 * 150% ) = 3, so a third seat is within the grace limit.
        await this.GetLeaseBodyAsync( server, "carol", "DESKTOP-3" );

        using var denied = await this.RequestLeaseAsync( server, "dave", "DESKTOP-4" );
        Assert.Equal( HttpStatusCode.Forbidden, denied.StatusCode );
    }

    [Fact]
    public async Task GracePeriodEndsAfterItsDays()
    {
        var server = this.CreateServer();
        server.MaxSeats = 1;
        server.GracePercent = 100;
        server.GraceDays = 2;
        server.LeaseDuration = TimeSpan.FromDays( 30 );

        await this.GetLeaseBodyAsync( server, "alice", "DESKTOP-1" );
        await this.GetLeaseBodyAsync( server, "bob", "DESKTOP-2" );

        this.Time.AddTime( TimeSpan.FromDays( 3 ) );

        using var denied = await this.RequestLeaseAsync( server, "carol", "DESKTOP-3" );
        Assert.Equal( HttpStatusCode.Forbidden, denied.StatusCode );
    }

    [Fact]
    public async Task UnlimitedSeatsNeverDeny()
    {
        var server = this.CreateServer();

        for ( var i = 0; i < 20; i++ )
        {
            await this.GetLeaseBodyAsync( server, $"user{i}", $"DESKTOP-{i:x}" );
        }

        Assert.Equal( 20, server.OccupiedSeatCount );
    }

    [Fact]
    public async Task ReleaseAllSeatsFreesEveryone()
    {
        var server = this.CreateServer();
        server.MaxSeats = 1;

        await this.GetLeaseBodyAsync( server, "alice", "DESKTOP-1" );
        server.ReleaseAllSeats();

        await this.GetLeaseBodyAsync( server, "bob", "DESKTOP-2" );

        Assert.Equal( 1, server.OccupiedSeatCount );
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Faults.
    // ---------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task UnreachableServerThrowsTheTransportException()
    {
        var server = this.CreateServer();
        server.FaultMode = LicenseServerFault.Unreachable;

        using var client = this.HttpClientFactory.Create();

        await Assert.ThrowsAsync<HttpRequestException>( () => client.GetAsync( server.LeaseUrl + "?user=a&machine=b" ) );
    }

    /// <summary>
    /// Tests that the timeout fault raises the very exception that <see cref="HttpClient"/> raises when its own
    /// timeout elapses, so that the client meets the same shape as in production and the test waits for nothing.
    /// </summary>
    /// <remarks>
    /// Only the type is asserted, because the two target frameworks disagree on the rest: .NET wraps the timeout in
    /// the <see cref="Exception.InnerException"/> of the cancellation, whereas .NET Framework raises a bare
    /// <see cref="TaskCanceledException"/> and the inner exception is lost. A client must therefore recognize a
    /// timeout from the type alone and never from what is nested inside it.
    /// </remarks>
    [Fact]
    public async Task TimeoutThrowsTheSameExceptionAsHttpClient()
    {
        var server = this.CreateServer();
        server.FaultMode = LicenseServerFault.Timeout;

        using var client = this.HttpClientFactory.Create();

        await Assert.ThrowsAnyAsync<OperationCanceledException>( () => client.GetAsync( server.LeaseUrl + "?user=a&machine=b" ) );
    }

    [Theory]
    [InlineData( LicenseServerFault.BadRequest, HttpStatusCode.BadRequest )]
    [InlineData( LicenseServerFault.Forbidden, HttpStatusCode.Forbidden )]
    [InlineData( LicenseServerFault.NotFound, HttpStatusCode.NotFound )]
    [InlineData( LicenseServerFault.InternalServerError, HttpStatusCode.InternalServerError )]
    [InlineData( LicenseServerFault.ServiceUnavailable, HttpStatusCode.ServiceUnavailable )]
    public async Task StatusCodeFaultsAnswerWithTheirStatus( LicenseServerFault fault, HttpStatusCode expectedStatusCode )
    {
        var server = this.CreateServer();
        server.FaultMode = fault;

        using var response = await this.RequestLeaseAsync( server );

        Assert.Equal( expectedStatusCode, response.StatusCode );
    }

    [Theory]
    [InlineData( LicenseServerFault.GarbageResponse )]
    [InlineData( LicenseServerFault.EmptyResponse )]
    [InlineData( LicenseServerFault.MissingLicenseKey )]
    public async Task UnusableBodiesDoNotParse( LicenseServerFault fault )
    {
        var server = this.CreateServer();
        server.FaultMode = fault;

        using var response = await this.RequestLeaseAsync( server );
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal( HttpStatusCode.OK, response.StatusCode );
        Assert.False( LicenseLease.TryDeserialize( body, this.Time.UtcNow, out _ ) );
    }

    /// <summary>
    /// Tests the server whose clock is behind the client's, which leases a licence that has already expired. Without a
    /// guard the client would download, discard and download again on every build.
    /// </summary>
    [Fact]
    public async Task ExpiredLeaseFaultServesALeaseThatHasAlreadyEnded()
    {
        var server = this.CreateServer();
        server.FaultMode = LicenseServerFault.ExpiredLease;

        var body = await this.GetLeaseBodyAsync( server );

        Assert.True( LicenseLease.TryDeserialize( body, this.Time.UtcNow, out var lease ) );
        Assert.True( lease.EndTime < this.Time.UtcNow );
    }

    /// <summary>
    /// Tests that the fault applies only after the requests that the test wants to succeed, which is how a test lets
    /// the first acquisition work and makes the renewal fail.
    /// </summary>
    [Fact]
    public async Task FaultAppliesOnlyAfterTheGivenNumberOfRequests()
    {
        var server = this.CreateServer();
        server.FaultMode = LicenseServerFault.InternalServerError;
        server.FaultAfterRequestCount = 1;

        using ( var first = await this.RequestLeaseAsync( server ) )
        {
            Assert.Equal( HttpStatusCode.OK, first.StatusCode );
        }

        using var second = await this.RequestLeaseAsync( server );
        Assert.Equal( HttpStatusCode.InternalServerError, second.StatusCode );
    }

    [Fact]
    public async Task ResponseFactoryOverridesEverything()
    {
        var server = this.CreateServer();
        server.MaxSeats = 0;
        server.ResponseFactory = _ => new HttpResponseMessage( HttpStatusCode.OK ) { Content = new StringContent( "License: OVERRIDDEN" ) };

        Assert.Equal( "License: OVERRIDDEN", await this.GetLeaseBodyAsync( server ) );
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Response shape.
    // ---------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task OmittedInstantsAreAbsentFromTheBody()
    {
        var server = this.CreateServer();
        server.IncludesStartTime = false;
        server.IncludesEndTime = false;
        server.IncludesRenewTime = false;

        var body = await this.GetLeaseBodyAsync( server );

        Assert.Equal( "License: KEY", body );
    }

    [Fact]
    public async Task ExtraPartsAreAppended()
    {
        var server = this.CreateServer();
        server.ExtraResponseParts = "Seats: 3";

        Assert.EndsWith( "; Seats: 3", await this.GetLeaseBodyAsync( server ), StringComparison.Ordinal );
    }

    /// <summary>
    /// Tests that the server can lease a different key on renewal, which is what proves that a client adopts the
    /// renewed lease rather than keeping the cached one.
    /// </summary>
    [Fact]
    public async Task LicenseKeyCanChangeBetweenRequests()
    {
        var server = this.CreateServer();

        Assert.Contains( "License: KEY", await this.GetLeaseBodyAsync( server ), StringComparison.Ordinal );

        server.LicenseKey = "RENEWED";

        Assert.Contains( "License: RENEWED", await this.GetLeaseBodyAsync( server ), StringComparison.Ordinal );
    }

    [Fact]
    public async Task LicenseKeySelectorLeasesPerUser()
    {
        var server = this.CreateServer();
        server.LicenseKeySelector = request => request.User == "alice" ? "ALICE" : "OTHER";

        Assert.Contains( "License: ALICE", await this.GetLeaseBodyAsync( server, "alice" ), StringComparison.Ordinal );
        Assert.Contains( "License: OTHER", await this.GetLeaseBodyAsync( server, "bob" ), StringComparison.Ordinal );
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Assertions and bookkeeping.
    // ---------------------------------------------------------------------------------------------------------------

    [Fact]
    public void AssertNotContactedPassesOnAFreshServer()
    {
        this.CreateServer().AssertNotContacted();
    }

    [Fact]
    public async Task AssertNotContactedFailsAfterARequest()
    {
        var server = this.CreateServer();
        await this.GetLeaseBodyAsync( server );

        Assert.Throws<InvalidOperationException>( server.AssertNotContacted );
    }

    [Fact]
    public async Task ClearRequestsForgetsTheRequestsButKeepsTheSeats()
    {
        var server = this.CreateServer();
        server.MaxSeats = 1;

        await this.GetLeaseBodyAsync( server, "alice", "DESKTOP-1" );
        server.ClearRequests();

        server.AssertNotContacted();
        Assert.Equal( 1, server.OccupiedSeatCount );
    }

    /// <summary>
    /// Tests that a disabled server answers nothing, so that the request falls through to whatever the test registered
    /// before it.
    /// </summary>
    [Fact]
    public async Task DisabledServerDoesNotAnswer()
    {
        var server = this.CreateServer();
        server.IsEnabled = false;

        using var response = await this.RequestLeaseAsync( server );

        Assert.Contains( "No hook was registered", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal );
        server.AssertNotContacted();
    }
}
