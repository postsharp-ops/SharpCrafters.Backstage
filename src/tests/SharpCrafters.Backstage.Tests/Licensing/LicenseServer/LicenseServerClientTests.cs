// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Licensing;
using SharpCrafters.Backstage.Licensing.LicenseServer;
using SharpCrafters.Backstage.Testing;
using SharpCrafters.Backstage.Utilities;
using System;
using System.Globalization;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace SharpCrafters.Backstage.Tests.Licensing.LicenseServer;

/// <summary>
/// Tests the client that obtains a lease from a license server: the request it sends, the cache it honours, and the
/// failures it reports.
/// </summary>
public sealed class LicenseServerClientTests : LicensingTestsBase
{
    private static readonly DateTime _start = new( 2026, 6, 1, 12, 0, 0, DateTimeKind.Utc );
    private static readonly DateTime _buildDate = new( 2026, 1, 15, 0, 0, 0, DateTimeKind.Utc );

    private LicenseServerClient _client = null!;
    private LicenseLeaseStore _leaseStore = null!;

    public LicenseServerClientTests( ITestOutputHelper logger ) : base( logger )
    {
        // The version and the build date reach the query string, so they are pinned here rather than inherited.
        this.ApplicationInfo = new TestApplicationInfo( "Licensing Test App", false, "2027.0.1", _buildDate );

        this.Time.Set( _start, false );
        this.UserIdentity.UserName = "testuser";
        this.UserIdentity.MachineName = "TEST-MACHINE";
    }

    protected override void OnAfterServicesCreated( Services services )
    {
        base.OnAfterServicesCreated( services );

        this._leaseStore = new LicenseLeaseStore( services.ServiceProvider );
        this._client = new LicenseServerClient( services.ServiceProvider, LicensingInitializationOptions.Default );
    }

    private LicenseServerClient Client
    {
        get
        {
            this.EnsureServicesInitialized();

            return this._client;
        }
    }

    private LicenseLeaseStore LeaseStore
    {
        get
        {
            this.EnsureServicesInitialized();

            return this._leaseStore;
        }
    }

    private LicenseServerSimulator CreateServer( string url = LicenseServerSimulator.DefaultUrl )
    {
        this.EnsureServicesInitialized();

        return new LicenseServerSimulator( this.HttpClientFactory, this.Time, url ) { LicenseKey = "LEASED-KEY" };
    }

    // ---------------------------------------------------------------------------------------------------------------
    // The request contract. These pin the exact wire request, which deployed servers parse.
    // ---------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Tests the whole request, argument by argument and in order. The hash of the machine identifier is a hard-coded
    /// literal rather than a value recomputed by the test, so that a change to the hashing algorithm fails here: the
    /// value has to stay equal to the one PostSharp sends, or a server accounts one machine as two.
    /// </summary>
    [Fact]
    public async Task RequestIsExactlyAsSpecified()
    {
        var server = this.CreateServer();

        await this.Client.GetLeaseAsync( server.Url, LicenseProduct.MetalamaProfessional );

        var expectedHash = HashUtilities.ComputeStringHash64( TestMachineIdProvider.DefaultMachineId ).ToString( "x", CultureInfo.InvariantCulture );

        Assert.Equal(
            "https://license.test/Lease.ashx?user=testuser&machine=TEST-MACHINE-"
            + expectedHash
            + "&version=2027.0.1&buildDate=2026-01-15T00%3A00%3A00.0000000Z&product=MetalamaProfessional",
            server.LastRequest!.RequestUri.OriginalString );
    }

    /// <summary>
    /// Tests that the machine hash is the value that PostSharp computes for the same machine identifier, so that a
    /// server which already accounts a seat for this machine keeps accounting one.
    /// </summary>
    [Fact]
    public async Task MachineHashIsThePostSharpHashOfTheMachineIdentifier()
    {
        var server = this.CreateServer();
        this.MachineIdProvider.MachineId = "7f3a1c68-2b4e-4d19-9a5c-0e6b8d2f4a71";

        await this.Client.GetLeaseAsync( server.Url );

        Assert.Equal(
            HashUtilities.ComputeStringHash64( "7f3a1c68-2b4e-4d19-9a5c-0e6b8d2f4a71" ).ToString( "x", CultureInfo.InvariantCulture ),
            server.LastRequest!.MachineHash );
    }

    [Fact]
    public async Task MachineHashIsLowerCaseHexadecimal()
    {
        var server = this.CreateServer();

        await this.Client.GetLeaseAsync( server.Url );

        Assert.Matches( "^[0-9a-f]+$", server.LastRequest!.MachineHash );
    }

    /// <summary>
    /// Tests that the product is omitted when the caller names none, which a server reads as "any product".
    /// </summary>
    [Fact]
    public async Task ProductIsOmittedWhenNotGiven()
    {
        var server = this.CreateServer();

        await this.Client.GetLeaseAsync( server.Url );

        Assert.Null( server.LastRequest!.Product );
        Assert.Equal( 4, server.LastRequest.QueryArguments.Count );
    }

    /// <summary>
    /// Tests that a user name carrying a domain and characters that are special in a query string survives the round
    /// trip, rather than truncating the query or producing a spurious argument.
    /// </summary>
    [Theory]
    [InlineData( "CONTOSO\\alice" )]
    [InlineData( "user name" )]
    [InlineData( "a&b=c" )]
    [InlineData( "ünïcodé" )]
    public async Task SpecialCharactersInTheUserNameAreEscaped( string userName )
    {
        var server = this.CreateServer();
        this.UserIdentity.UserName = userName;

        await this.Client.GetLeaseAsync( server.Url );

        Assert.Equal( userName, server.LastRequest!.User );
        Assert.Equal( 4, server.LastRequest.QueryArguments.Count );
    }

    [Fact]
    public async Task BuildDateUsesTheRoundTripFormat()
    {
        var server = this.CreateServer();

        await this.Client.GetLeaseAsync( server.Url );

        Assert.Equal( _buildDate, server.LastRequest!.BuildDateValue );
    }

    /// <summary>
    /// Tests that an application which declares no build date still sends one, because a server fails to parse an
    /// empty argument.
    /// </summary>
    [Fact]
    public async Task MissingBuildDateIsStillSent()
    {
        ((TestApplicationInfo) this.ApplicationInfo).BuildDate = null;
        var server = this.CreateServer();

        await this.Client.GetLeaseAsync( server.Url );

        Assert.NotNull( server.LastRequest!.BuildDateValue );
    }

    [Theory]
    [InlineData( "https://license.test" )]
    [InlineData( "https://license.test/" )]
    public async Task TrailingSlashDoesNotDoubleTheSeparator( string url )
    {
        var server = this.CreateServer();

        await this.Client.GetLeaseAsync( url );

        Assert.DoesNotContain( "//Lease.ashx", server.LastRequest!.RequestUri.OriginalString, StringComparison.Ordinal );
        server.AssertContacted();
    }

    [Fact]
    public async Task PathOfTheServerUrlIsPreserved()
    {
        var server = this.CreateServer( "https://license.test/postsharp" );

        var result = await this.Client.GetLeaseAsync( server.Url );

        Assert.True( result.IsSuccess );
        Assert.Equal( "/postsharp/Lease.ashx", server.LastRequest!.RequestUri.AbsolutePath );
    }

    /// <summary>
    /// Tests that the client asks for a connection that authenticates with the credentials of the current user. An
    /// on-premises license server is typically published with Windows authentication and answers 401 otherwise. The
    /// handshake itself cannot be exercised in process, so the configuration is what is asserted.
    /// </summary>
    [Fact]
    public async Task RequestUsesTheCredentialsOfTheCurrentUser()
    {
        var server = this.CreateServer();

        await this.Client.GetLeaseAsync( server.Url );

        Assert.NotNull( this.HttpClientFactory.LastOptions );
        Assert.True( this.HttpClientFactory.LastOptions!.UseDefaultCredentials );
        Assert.Equal( TimeSpan.FromSeconds( 10 ), this.HttpClientFactory.LastOptions.Timeout );
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Acquisition, cache and renewal.
    // ---------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task LeaseIsAcquiredAndStored()
    {
        var server = this.CreateServer();

        var result = await this.Client.GetLeaseAsync( server.Url );

        Assert.True( result.IsSuccess );
        Assert.Equal( "LEASED-KEY", result.Lease!.LicenseKey );
        Assert.True( this.LeaseStore.TryGetLease( server.Url, out var storedLease ) );
        Assert.Equal( result.Lease, storedLease );
    }

    /// <summary>
    /// Tests that a stored lease that is still valid and not yet due for renewal costs no request. This is the whole
    /// point of storing it: a build that has a licence must not contact anything.
    /// </summary>
    [Fact]
    public async Task ValidStoredLeaseCostsNoRequest()
    {
        var server = this.CreateServer();

        await this.Client.GetLeaseAsync( server.Url );
        server.ClearRequests();

        var result = await this.Client.GetLeaseAsync( server.Url );

        Assert.True( result.IsSuccess );
        server.AssertNotContacted();
    }

    [Fact]
    public async Task LeaseIsNotRenewedBeforeItsRenewTime()
    {
        var server = this.CreateServer();

        await this.Client.GetLeaseAsync( server.Url );
        server.ClearRequests();

        // The default lease lasts three days and is due for renewal after two.
        this.Time.AddTime( TimeSpan.FromDays( 2 ) - TimeSpan.FromMinutes( 1 ) );
        await this.Client.GetLeaseAsync( server.Url );

        server.AssertNotContacted();
    }

    [Fact]
    public async Task LeaseIsRenewedAfterItsRenewTime()
    {
        var server = this.CreateServer();

        await this.Client.GetLeaseAsync( server.Url );
        server.ClearRequests();

        this.Time.AddTime( TimeSpan.FromDays( 2 ) + TimeSpan.FromMinutes( 1 ) );
        var result = await this.Client.GetLeaseAsync( server.Url );

        Assert.True( result.IsSuccess );
        server.AssertContacted();
    }

    /// <summary>
    /// Tests that the renewed lease is the one adopted, and not the stored one. The server hands out a different key
    /// on the renewal, which is what makes the difference observable.
    /// </summary>
    [Fact]
    public async Task RenewedLeaseReplacesTheStoredOne()
    {
        var server = this.CreateServer();

        await this.Client.GetLeaseAsync( server.Url );
        server.LicenseKey = "RENEWED-KEY";

        this.Time.AddTime( TimeSpan.FromDays( 2 ) + TimeSpan.FromMinutes( 1 ) );
        var result = await this.Client.GetLeaseAsync( server.Url );

        Assert.Equal( "RENEWED-KEY", result.Lease!.LicenseKey );
        Assert.True( this.LeaseStore.TryGetLease( server.Url, out var storedLease ) );
        Assert.Equal( "RENEWED-KEY", storedLease.LicenseKey );
    }

    [Fact]
    public async Task ExpiredLeaseIsReplacedByANewOne()
    {
        var server = this.CreateServer();

        await this.Client.GetLeaseAsync( server.Url );
        server.LicenseKey = "SECOND-KEY";
        server.ClearRequests();

        this.Time.AddTime( TimeSpan.FromDays( 3 ) + TimeSpan.FromMinutes( 1 ) );
        var result = await this.Client.GetLeaseAsync( server.Url );

        server.AssertContacted();
        Assert.Equal( "SECOND-KEY", result.Lease!.LicenseKey );
    }

    /// <summary>
    /// Tests the boundary of the validity of a stored lease: it is usable up to and including the instant at which it
    /// ends, and not beyond.
    /// </summary>
    [Theory]
    [InlineData( 0, false )]
    [InlineData( 1, true )]
    public async Task ExpiryBoundaryIsInclusive( int ticksPastTheEnd, bool expectedContacted )
    {
        var server = this.CreateServer();
        server.RenewPeriod = TimeSpan.FromDays( 3 );

        await this.Client.GetLeaseAsync( server.Url );
        server.ClearRequests();

        this.Time.Set( _start.AddDays( 3 ).AddTicks( ticksPastTheEnd ), false );
        await this.Client.GetLeaseAsync( server.Url );

        if ( expectedContacted )
        {
            server.AssertContacted();
        }
        else
        {
            server.AssertNotContacted();
        }
    }

    [Fact]
    public async Task LeasesOfTwoServersAreIndependent()
    {
        var primary = this.CreateServer( "https://primary.license.test" );
        var secondary = this.CreateServer( "https://secondary.license.test" );
        primary.LicenseKey = "PRIMARY";
        secondary.LicenseKey = "SECONDARY";

        Assert.Equal( "PRIMARY", (await this.Client.GetLeaseAsync( primary.Url )).Lease!.LicenseKey );
        Assert.Equal( "SECONDARY", (await this.Client.GetLeaseAsync( secondary.Url )).Lease!.LicenseKey );

        primary.AssertContacted();
        secondary.AssertContacted();
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Failures.
    // ---------------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData( LicenseServerFault.Unreachable )]
    [InlineData( LicenseServerFault.Timeout )]
    [InlineData( LicenseServerFault.BadRequest )]
    [InlineData( LicenseServerFault.NotFound )]
    [InlineData( LicenseServerFault.InternalServerError )]
    [InlineData( LicenseServerFault.ServiceUnavailable )]
    [InlineData( LicenseServerFault.GarbageResponse )]
    [InlineData( LicenseServerFault.EmptyResponse )]
    [InlineData( LicenseServerFault.MissingLicenseKey )]
    [InlineData( LicenseServerFault.ExpiredLease )]
    public async Task FailureIsReportedAndNothingIsStored( LicenseServerFault fault )
    {
        var server = this.CreateServer();
        server.FaultMode = fault;

        var result = await this.Client.GetLeaseAsync( server.Url );

        Assert.False( result.IsSuccess );
        Assert.False( string.IsNullOrEmpty( result.ErrorMessage ) );
        Assert.False( this.LeaseStore.TryGetLease( server.Url, out _ ) );
    }

    /// <summary>
    /// Tests that the body of an HTTP 403 reaches the user verbatim. This is how a license server explains that a
    /// team has no seat left, and it is the only thing that tells the user what to do about it.
    /// </summary>
    [Fact]
    public async Task DenialMessageOfTheServerIsReportedVerbatim()
    {
        var server = this.CreateServer();
        server.FaultMode = LicenseServerFault.Forbidden;
        server.DenialMessage = "No license with free capacity. Contact your administrator.";

        var result = await this.Client.GetLeaseAsync( server.Url );

        Assert.False( result.IsSuccess );
        Assert.Equal( server.DenialMessage, result.ErrorMessage );
    }

    [Fact]
    public async Task EmptyDenialStillReportsSomething()
    {
        var server = this.CreateServer();
        server.FaultMode = LicenseServerFault.Forbidden;
        server.DenialMessage = "";

        var result = await this.Client.GetLeaseAsync( server.Url );

        Assert.False( result.IsSuccess );
        Assert.Contains( "refused to grant a lease", result.ErrorMessage, StringComparison.Ordinal );
    }

    [Theory]
    [InlineData( LicenseServerFault.GarbageResponse )]
    [InlineData( LicenseServerFault.EmptyResponse )]
    [InlineData( LicenseServerFault.MissingLicenseKey )]
    public async Task UnusableBodyIsReportedAsAnInvalidResponse( LicenseServerFault fault )
    {
        var server = this.CreateServer();
        server.FaultMode = fault;

        var result = await this.Client.GetLeaseAsync( server.Url );

        Assert.Contains( "returned an invalid response", result.ErrorMessage, StringComparison.Ordinal );
    }

    /// <summary>
    /// Tests the server whose clock is behind the client's. Without this guard every build would download a lease,
    /// find it already expired, discard it and download again.
    /// </summary>
    [Fact]
    public async Task LeaseThatHasAlreadyEndedIsRejected()
    {
        var server = this.CreateServer();
        server.FaultMode = LicenseServerFault.ExpiredLease;

        var result = await this.Client.GetLeaseAsync( server.Url );

        Assert.False( result.IsSuccess );
        Assert.Contains( "clock of the server may be wrong", result.ErrorMessage, StringComparison.Ordinal );
    }

    [Fact]
    public async Task TimeoutIsReportedRatherThanRaised()
    {
        var server = this.CreateServer();
        server.FaultMode = LicenseServerFault.Timeout;

        var result = await this.Client.GetLeaseAsync( server.Url );

        Assert.False( result.IsSuccess );
        Assert.Contains( "did not answer within", result.ErrorMessage, StringComparison.Ordinal );
    }

    /// <summary>
    /// Tests that a stored lease that is still valid survives a failed renewal, and that the failure is not reported
    /// to the caller. A build that has a licence must not fail because the server was briefly unreachable.
    /// </summary>
    [Fact]
    public async Task FailedRenewalKeepsTheStoredLease()
    {
        var server = this.CreateServer();

        await this.Client.GetLeaseAsync( server.Url );

        server.FaultMode = LicenseServerFault.Unreachable;
        this.Time.AddTime( TimeSpan.FromDays( 2 ) + TimeSpan.FromMinutes( 1 ) );

        var result = await this.Client.GetLeaseAsync( server.Url );

        Assert.True( result.IsSuccess );
        Assert.Equal( "LEASED-KEY", result.Lease!.LicenseKey );
        Assert.Null( result.ErrorMessage );
    }

    /// <summary>
    /// Tests that a stored lease that has ended is not used when the server cannot be reached: the lease is over, and
    /// reporting no licence is the honest outcome.
    /// </summary>
    [Fact]
    public async Task ExpiredStoredLeaseIsNotUsedWhenTheServerIsUnreachable()
    {
        var server = this.CreateServer();

        await this.Client.GetLeaseAsync( server.Url );

        server.FaultMode = LicenseServerFault.Unreachable;
        this.Time.AddTime( TimeSpan.FromDays( 3 ) + TimeSpan.FromMinutes( 1 ) );

        var result = await this.Client.GetLeaseAsync( server.Url );

        Assert.False( result.IsSuccess );
        Assert.False( this.LeaseStore.TryGetLease( server.Url, out _ ) );
    }

    [Fact]
    public async Task RepeatedFailureDoesNotRaise()
    {
        var server = this.CreateServer();
        server.FaultMode = LicenseServerFault.Unreachable;

        for ( var i = 0; i < 3; i++ )
        {
            Assert.False( (await this.Client.GetLeaseAsync( server.Url )).IsSuccess );
        }

        server.AssertContacted( 3 );
    }

    /// <summary>
    /// Tests that a lease served without the optional instants gets the defaults of the parser, so that a minimal
    /// server implementation still yields a usable lease.
    /// </summary>
    [Fact]
    public async Task LeaseWithoutInstantsUsesTheDefaults()
    {
        var server = this.CreateServer();
        server.IncludesStartTime = false;
        server.IncludesEndTime = false;
        server.IncludesRenewTime = false;

        var result = await this.Client.GetLeaseAsync( server.Url );

        Assert.True( result.IsSuccess );
        Assert.Equal( _start, result.Lease!.StartTime );
        Assert.Equal( _start.AddDays( 1 ), result.Lease.EndTime );
    }
}
