// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Testing;
using System;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace SharpCrafters.Backstage.Commands.Tests.Commands.Licensing;

/// <summary>
/// Tests the commands that register, list and diagnose a license server.
/// </summary>
/// <remarks>
/// The application pinned here declares a version at or above the one a registered license server URL is stored
/// under, because an earlier version deliberately skips that group; the default of the test base is 0.0.
/// </remarks>
public sealed class LicenseServerCommandTests : LicensingCommandsTestsBase
{
    private const string _url = "https://license.test";
    private const string _insecureUrl = "http://insecure.license.test";

    private readonly LicenseServerSimulator _server;

    public LicenseServerCommandTests( ITestOutputHelper logger ) : base(
        logger,
        new TestApplicationInfo( "test", false, "2027.0.1", new DateTime( 2026, 1, 15, 0, 0, 0, DateTimeKind.Utc ) ) )
    {
        this.Time.Set( new DateTime( 2026, 6, 1, 12, 0, 0, DateTimeKind.Utc ), false );

        this._server = new LicenseServerSimulator( this.HttpClientFactory, this.Time, _url )
        {
            LicenseKey = LicenseKeyProvider.MetalamaEnterpriseLicenseServerEligible
        };
    }

    /// <summary>
    /// Creates a second simulator, reached over HTTP, so that the warning about an insecure server can be exercised
    /// through the commands that a user runs when they configure one.
    /// </summary>
    private LicenseServerSimulator CreateInsecureServer()
        => new( this.HttpClientFactory, this.Time, _insecureUrl ) { LicenseKey = LicenseKeyProvider.MetalamaEnterpriseLicenseServerEligible };

    // ---------------------------------------------------------------------------------------------------------------
    // register
    // ---------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Tests that a user who registers a license server is told they registered a server. What they registered is
    /// the address of a service their organization runs, not a key, and confusing the two makes the next support
    /// conversation start from the wrong place.
    /// </summary>
    [Fact]
    public async Task Register_ReportsTheServerAndNotAKey()
    {
        await this.TestCommandAsync( $"license register {_url}", expectedOutput: $"The license server '{_url}' has been registered." );
    }

    /// <summary>
    /// Tests that registering proves the server works. A registration that stored the address without asking the
    /// server anything would tell the user they are done, and leave them to discover on their next build that they
    /// are not.
    /// </summary>
    [Fact]
    public async Task Register_ContactsTheServer()
    {
        await this.TestCommandAsync( $"license register {_url}" );

        this._server.AssertContacted();
    }

    /// <summary>
    /// Tests that a user who mistypes the address, or whose server is not running, learns it while they are still
    /// at the command line, rather than the next time they build.
    /// </summary>
    [Fact]
    public async Task Register_UnreachableServer_Fails()
    {
        this._server.FaultMode = LicenseServerFault.Unreachable;

        await this.TestCommandAsync( $"license register {_url}", expectedOutput: "Cannot get a lease", expectedExitCode: 1 );
    }

    /// <summary>
    /// Tests that the explanation a server gives for refusing a lease reaches the console verbatim. That message is
    /// the only thing that tells the user what to do, so it must not be replaced by one of ours.
    /// </summary>
    [Fact]
    public async Task Register_DeniedByTheServer_ShowsTheMessageOfTheServer()
    {
        this._server.FaultMode = LicenseServerFault.Forbidden;
        this._server.DenialMessage = "No license with free capacity. Ask your administrator for a seat.";

        await this.TestCommandAsync( $"license register {_url}", expectedOutput: this._server.DenialMessage, expectedExitCode: 1 );
    }

    /// <summary>
    /// Tests the address that answers but is not a license server, which is what a user gets when they point at the
    /// wrong service on the right host. It must be refused rather than registered.
    /// </summary>
    [Fact]
    public async Task Register_GarbageResponse_Fails()
    {
        this._server.FaultMode = LicenseServerFault.GarbageResponse;

        await this.TestCommandAsync( $"license register {_url}", expectedOutput: "invalid response", expectedExitCode: 1 );
    }

    /// <summary>
    /// Tests that an address the product can never use is refused with the reason it can never be used, so that the
    /// user corrects the address rather than looking for a fault in their server.
    /// </summary>
    [Theory]
    [InlineData( "https://license.test?x=1", "query string" )]
    [InlineData( "ftp://license.test", "HTTP and HTTPS" )]
    public async Task Register_MalformedUrl_ReportsTheReason( string url, string expectedOutput )
    {
        await this.TestCommandAsync( $"license register {url}", expectedOutput: expectedOutput, expectedExitCode: 1 );
    }

    /// <summary>
    /// Tests that registering a server reached over HTTP succeeds and warns. Registering is the moment the user can
    /// still choose a different URL, which is why the warning belongs here and not only to the builds that follow.
    /// </summary>
    [Fact]
    public async Task Register_InsecureServer_SucceedsAndWarns()
    {
        _ = this.CreateInsecureServer();

        await this.TestCommandAsync( $"license register {_insecureUrl}", expectedOutput: "has been registered" );
        await this.TestCommandAsync( $"license register {_insecureUrl}", expectedOutput: "cleartext" );
    }

    /// <summary>
    /// Tests that a properly secured server is registered in silence. A warning on the ordinary case teaches users
    /// to ignore warnings, including the one that matters.
    /// </summary>
    [Fact]
    public async Task Register_SecureServer_DoesNotWarn()
    {
        await this.TestCommandAsync( $"license register {_url}", unexpectedOutput: "cleartext" );
    }

    // ---------------------------------------------------------------------------------------------------------------
    // list
    // ---------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Tests that the list shows the server a user registered and the period it has licensed them for. It is how
    /// they check what licenses their builds and how long they have before the product asks the server again.
    /// </summary>
    [Fact]
    public async Task List_ShowsTheServerAndItsLease()
    {
        await this.TestCommandAsync( $"license register {_url}" );

        await this.TestCommandAsync( "license list", expectedOutput: "The following license server is currently registered" );
        await this.TestCommandAsync( "license list", expectedOutput: "License Server" );
        await this.TestCommandAsync( "license list", expectedOutput: _url );
        await this.TestCommandAsync( "license list", expectedOutput: "Lease Expiration" );
        await this.TestCommandAsync( "license list", expectedOutput: "Lease Renewal" );
    }

    /// <summary>
    /// Tests that the product of the leased licence is shown, so that the user sees what the server gives them and not
    /// merely that a server is configured.
    /// </summary>
    [Fact]
    public async Task List_ShowsTheProductOfTheLeasedLicense()
    {
        await this.TestCommandAsync( $"license register {_url}" );

        await this.TestCommandAsync( "license list", expectedOutput: "Metalama Enterprise" );
    }

    /// <summary>
    /// Tests that the leased licence key is not presented as a registered license key. The user did not register it,
    /// it expires, and they cannot do anything with it.
    /// </summary>
    [Fact]
    public async Task List_DoesNotShowTheLeasedLicenseKey()
    {
        await this.TestCommandAsync( $"license register {_url}" );

        await this.TestCommandAsync(
            "license list",
            unexpectedOutput: LicenseKeyProvider.MetalamaEnterpriseLicenseServerEligible );
    }

    /// <summary>
    /// Tests that listing contacts no server, so that showing what is registered never waits for a network.
    /// </summary>
    [Fact]
    public async Task List_ContactsNoServer()
    {
        await this.TestCommandAsync( $"license register {_url}" );
        this._server.ClearRequests();

        await this.TestCommandAsync( "license list" );

        this._server.AssertNotContacted();
    }

    /// <summary>
    /// Tests that listing still works, and still names the server, when the server has become unreachable since it was
    /// registered: the command reports what is registered, not what the server says today.
    /// </summary>
    [Fact]
    public async Task List_WorksWhenTheServerIsDown()
    {
        await this.TestCommandAsync( $"license register {_url}" );

        this._server.FaultMode = LicenseServerFault.Unreachable;

        await this.TestCommandAsync( "license list", expectedOutput: _url );
    }

    /// <summary>
    /// Tests that a group of license keys that the current version does not support does not hide a registered license
    /// server. See issue #1922.
    /// </summary>
    [Fact]
    public async Task List_UnsupportedGroup_DoesNotHideTheServer()
    {
        await this.TestCommandAsync( $"license register {_url}" );

        this.AddUnsupportedLicenseGroup();

        await this.TestCommandAsync( "license list", expectedOutput: _url );
        await this.TestCommandAsync( "license list", expectedOutput: $"requires Metalama {UnsupportedVersion} or later" );
    }

    // ---------------------------------------------------------------------------------------------------------------
    // unregister
    // ---------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Tests that a user who unregisters stops being licensed by the server, and is told so. The command is how
    /// someone hands a machine back or moves to a licence key of their own.
    /// </summary>
    [Fact]
    public async Task Unregister_RemovesTheServer()
    {
        await this.TestCommandAsync( $"license register {_url}" );
        await this.TestCommandAsync( "license unregister", expectedOutput: "All license keys and license servers have been unregistered." );

        await this.TestCommandAsync( "license list", expectedOutput: "No Metalama license is currently registered." );
    }

    /// <summary>
    /// Tests that unregistering really stops the product from using the server, rather than leaving it licensed until
    /// the lease it already holds runs out.
    /// </summary>
    [Fact]
    public async Task Unregister_StopsUsingTheServer()
    {
        await this.TestCommandAsync( $"license register {_url}" );
        await this.TestCommandAsync( "license unregister" );
        this._server.ClearRequests();

        await this.TestCommandAsync( "license list" );

        this._server.AssertNotContacted();
    }

    // ---------------------------------------------------------------------------------------------------------------
    // acquire-lease
    // ---------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Tests that the user is shown which product their license server grants them. That is the question the
    /// command exists to answer, and the first one support asks.
    /// </summary>
    [Fact]
    public async Task AcquireLease_ShowsTheLeasedLicense()
    {
        await this.TestCommandAsync( $"license register {_url}" );

        await this.TestCommandAsync( "license acquire-lease", expectedOutput: "leases the following license" );
        await this.TestCommandAsync( "license acquire-lease", expectedOutput: "Metalama Enterprise" );
    }

    /// <summary>
    /// Tests that the command without a registered license server says so, rather than reporting something that reads
    /// as a problem with a server.
    /// </summary>
    [Fact]
    public async Task AcquireLease_WithoutARegisteredServer_SaysSo()
    {
        await this.TestCommandAsync( "license acquire-lease", expectedOutput: "No license server is registered", expectedExitCode: 1 );
    }

    /// <summary>
    /// Tests that the command costs what a build costs: a lease that is still good is used as it stands, so running it
    /// does not take a further seat.
    /// </summary>
    [Fact]
    public async Task AcquireLease_WithAValidLease_ContactsNothing()
    {
        await this.TestCommandAsync( $"license register {_url}" );
        this._server.ClearRequests();

        await this.TestCommandAsync( "license acquire-lease" );

        this._server.AssertNotContacted();
    }

    /// <summary>
    /// Tests that a user can make the product talk to the server on demand. Without it, someone diagnosing a server
    /// from a machine that already holds a lease would watch the command report an old answer and learn nothing
    /// about the server they are trying to check.
    /// </summary>
    [Fact]
    public async Task AcquireLease_Force_RenewsALeaseThatIsNotDue()
    {
        await this.TestCommandAsync( $"license register {_url}" );
        this._server.ClearRequests();

        await this.TestCommandAsync( "license acquire-lease --force" );

        this._server.AssertContacted();
    }

    /// <summary>
    /// Tests that a user checking a server which is down is told it is down, which is the answer they ran the
    /// command to get.
    /// </summary>
    [Fact]
    public async Task AcquireLease_UnreachableServer_Fails()
    {
        await this.TestCommandAsync( $"license register {_url}" );

        this._server.FaultMode = LicenseServerFault.Unreachable;

        await this.TestCommandAsync( "license acquire-lease --force", expectedOutput: "Cannot get a lease", expectedExitCode: 1 );
    }

    /// <summary>
    /// Tests that the explanation an administrator configured for a refusal reaches the developer unaltered. Those
    /// words are how a company tells its own developers whom to ask for a seat.
    /// </summary>
    [Fact]
    public async Task AcquireLease_DeniedByTheServer_ShowsTheMessageOfTheServer()
    {
        await this.TestCommandAsync( $"license register {_url}" );

        this._server.FaultMode = LicenseServerFault.Forbidden;
        this._server.DenialMessage = "All 5 seats are in use.";

        await this.TestCommandAsync( "license acquire-lease --force", expectedOutput: this._server.DenialMessage, expectedExitCode: 1 );
    }

    /// <summary>
    /// Tests that a user leasing from a server reached without encryption is reminded that their name and the name
    /// of their machine travel in the clear, at the moment they are looking at that server.
    /// </summary>
    [Fact]
    public async Task AcquireLease_InsecureServer_Warns()
    {
        _ = this.CreateInsecureServer();

        await this.TestCommandAsync( $"license register {_insecureUrl}" );

        await this.TestCommandAsync( "license acquire-lease", expectedOutput: "cleartext" );
    }
}
