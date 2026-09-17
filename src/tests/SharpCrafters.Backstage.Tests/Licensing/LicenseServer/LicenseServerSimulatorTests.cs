// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Licensing.LicenseServer;
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

    /// <summary>
    /// Tests that what the simulator writes is what the product reads. The simulator writes the answer of a server
    /// by hand, on purpose, so that it cannot agree with the product about a format no deployed server speaks; this
    /// is the one test that holds the two ends together.
    /// </summary>
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
    /// Tests that the simulator remembers who asked for what. Every claim these tests make about what a build costs
    /// the customer -- which user, which machine, which product -- is read back from here.
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

    /// <summary>
    /// Tests that two simulated servers can stand for two real ones. A customer with a server per division is a
    /// deployment worth covering, and it can only be covered if the instrument can represent it.
    /// </summary>
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
    /// Tests that the simulated server tells a caller what time it is and how fast its clock runs. That is what
    /// lets a load simulation live through the weeks of leases and renewals that reveal how a real server behaves
    /// under a year of use.
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

    /// <summary>
    /// Tests that the simulated server keeps the time of the test rather than of the wall clock, so that a test can
    /// live through the weeks a lease takes to expire without waiting for them.
    /// </summary>
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

    /// <summary>
    /// Tests that seats are counted per person and not per machine. This is what the customer bought, and a
    /// simulator that counted otherwise would let a defect in the product pass unnoticed.
    /// </summary>
    [Fact]
    public async Task SecondUserTakesASecondSeat()
    {
        var server = this.CreateServer();
        server.MaxSeats = 2;

        await this.GetLeaseBodyAsync( server, "alice", "DESKTOP-1" );
        await this.GetLeaseBodyAsync( server, "bob", "DESKTOP-2" );

        Assert.Equal( 2, server.OccupiedSeatCount );
    }

    /// <summary>
    /// Tests that a team which has used every seat it bought is refused, and told so. Running out of seats is the
    /// situation this whole feature exists to manage, and the refusal is the moment the customer learns they need
    /// more.
    /// </summary>
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

    /// <summary>
    /// Tests that the words an administrator configured for a refusal reach the developer unaltered. Those words
    /// are how a company tells its own developers whom to ask for a seat.
    /// </summary>
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

    /// <summary>
    /// Tests that keeping a machine licensed costs one seat and not one per renewal. A developer builds for years
    /// on the same machine, and a pool that drained as they worked would be unusable.
    /// </summary>
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

    /// <summary>
    /// Tests that the grace period is a period and not a permanent allowance. It exists so that a team which has
    /// just grown keeps building while the purchase goes through, and it has to end, or nobody would ever buy the
    /// seats.
    /// </summary>
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

    /// <summary>
    /// Tests the customer whose licence sets no limit on seats. Their developers must never be refused, whatever
    /// the size of the organization.
    /// </summary>
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

    /// <summary>
    /// Tests that the instrument can be returned to an empty pool, so that one test does not leave the seats it
    /// took to another and make a refusal look like a defect.
    /// </summary>
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

    /// <summary>
    /// Tests that a server nobody can reach fails the way an unreachable server really fails. It is the most common
    /// thing that goes wrong with an on-premises deployment -- a name that does not resolve, a port that is closed
    /// -- so the product has to be exercised against the real shape of it.
    /// </summary>
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

    /// <summary>
    /// Tests each way a server or the infrastructure in front of it turns a request away: a rejected argument, a
    /// refusal, a wrong address, a broken server, an overloaded one. The product has to tell them apart, because
    /// the person who can fix each one is a different person.
    /// </summary>
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

    /// <summary>
    /// Tests the answers that are not a lease at all, which is what a proxy, a sign-in page or a load balancer
    /// returns when it intercepts the request. They must not be mistaken for a licence.
    /// </summary>
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

    /// <summary>
    /// Tests that a test can make the simulator answer anything at all, so that a deployment nobody anticipated can
    /// be reproduced without changing the instrument itself.
    /// </summary>
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

    /// <summary>
    /// Tests that the simulator can answer with the licence key alone, as a minimal server implementation does. The
    /// protocol makes the rest optional, and a customer running such a server has to be able to build.
    /// </summary>
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

    /// <summary>
    /// Tests that the simulator can add a field the product does not know, so that the product can be shown to go
    /// on working against a server of a later version than itself.
    /// </summary>
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

    /// <summary>
    /// Tests that the simulator can grant a different licence to a different person, as a real server does when it
    /// draws from several pools. Without it, no test could tell whose licence a build ended up using.
    /// </summary>
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

    /// <summary>
    /// Tests that "the server was never contacted" is true of a server nobody has contacted. Many of the claims
    /// about what a build costs the customer rest on that assertion, so it is worth knowing it can pass.
    /// </summary>
    [Fact]
    public void AssertNotContactedPassesOnAFreshServer()
    {
        this.CreateServer().AssertNotContacted();
    }

    /// <summary>
    /// Tests that "the server was never contacted" fails once it has been. An assertion that cannot fail proves
    /// nothing, and this one is what guards the promise that a build with a valid lease costs no seat.
    /// </summary>
    [Fact]
    public async Task AssertNotContactedFailsAfterARequest()
    {
        var server = this.CreateServer();
        await this.GetLeaseBodyAsync( server );

        Assert.Throws<InvalidOperationException>( server.AssertNotContacted );
    }

    /// <summary>
    /// Tests that forgetting the requests does not give the seats back. A test that sets the scene and then
    /// measures what follows must not accidentally reset the pool it is measuring against.
    /// </summary>
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
    /// Tests that a simulated server can be taken out of service, so that a test can reproduce what happens to a
    /// customer whose server is switched off rather than merely unreachable.
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
