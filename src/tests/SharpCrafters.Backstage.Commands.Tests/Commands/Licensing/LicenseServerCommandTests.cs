// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Testing;
using System;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace SharpCrafters.Backstage.Commands.Tests.Commands.Licensing;

/// <summary>
/// Smoke tests for the commands that manage a license server: each one runs once, on the path a user takes when
/// everything works.
/// </summary>
/// <remarks>
/// <para>
/// These commands are shims over <c>ILicenseRegistrationService</c>. What a license server grants, what it refuses,
/// what happens when it is unreachable, when a lease is renewed and what a seat costs are rules of the licensing
/// API, and they are tested there, where a failure names the rule that broke rather than the command that surfaced
/// it. What is left here is the part only a command has: that it is wired to the service, and that it prints what
/// the user needs to see.
/// </para>
/// <para>
/// There is no license server simulator here, on purpose. A smoke test needs a server that answers, not one that
/// keeps seats and can be made to fail in twelve ways, and reaching for the simulator is how a smoke test grows into
/// a second copy of the tests of the API.
/// </para>
/// </remarks>
public sealed class LicenseServerCommandTests : LicensingCommandsTestsBase
{
    private const string _url = "https://license.test";

    private static readonly DateTime _now = new( 2026, 6, 1, 12, 0, 0, DateTimeKind.Utc );

    public LicenseServerCommandTests( ITestOutputHelper logger ) : base(
        logger,
        new TestApplicationInfo(
            "test",
            false,
            TestApplicationInfo.CurrentPackageVersion,
            new DateTime( 2026, 1, 15, 0, 0, 0, DateTimeKind.Utc ) ) )
    {
        this.Time.Set( _now, false );
    }

    /// <summary>
    /// Answers every lease request with one fixed licence, which is all a smoke test needs from a license server.
    /// </summary>
    private void ServeALease()
    {
        var body =
            $"License: {LicenseKeyProvider.MetalamaEnterpriseLicenseServerEligible}"
            + $"; StartTime: {_now.ToString( "o", CultureInfo.InvariantCulture )}"
            + $"; EndTime: {_now.AddDays( 3 ).ToString( "o", CultureInfo.InvariantCulture )}"
            + $"; RenewTime: {_now.AddDays( 2 ).ToString( "o", CultureInfo.InvariantCulture )}";

        this.HttpClientFactory.InsertHook(
            request => request.RequestUri!.AbsolutePath.EndsWith( "/Lease.ashx", StringComparison.OrdinalIgnoreCase ),
            ( _, _ ) => Task.FromResult( new HttpResponseMessage( HttpStatusCode.OK ) { Content = new StringContent( body ) } ) );
    }

    /// <summary>
    /// Tests that a user can register their license server and is told that is what they registered. What they gave
    /// is the address of a service their organization runs, not a licence key, and the two must not be confused.
    /// </summary>
    [Fact]
    public async Task Register_RegistersTheServer()
    {
        this.ServeALease();

        await this.TestCommandAsync( $"license register {_url}", expectedOutput: $"The license server '{_url}' has been registered." );
    }

    /// <summary>
    /// Tests that a user can see which license server licenses their builds and until when. This is the command
    /// someone runs first when they are asked what licenses their machine.
    /// </summary>
    [Fact]
    public async Task List_ShowsTheServerAndItsLease()
    {
        this.ServeALease();

        await this.TestCommandAsync( $"license register {_url}" );

        await this.TestCommandAsync( "license list", expectedOutput: _url );
        await this.TestCommandAsync( "license list", expectedOutput: "Lease Expiration" );
    }

    /// <summary>
    /// Tests that a user can ask their license server for a licence and be shown what it grants them. It is the
    /// command support asks for the output of.
    /// </summary>
    [Fact]
    public async Task AcquireLease_ShowsTheLeasedLicense()
    {
        this.ServeALease();

        await this.TestCommandAsync( $"license register {_url}" );

        await this.TestCommandAsync( "license acquire-lease", expectedOutput: "leases the following license" );
        await this.TestCommandAsync( "license acquire-lease", expectedOutput: "Metalama Enterprise" );
    }

    /// <summary>
    /// Tests that a user can stop being licensed by their license server, and is told so. It is how someone hands a
    /// machine back or moves to a licence of their own.
    /// </summary>
    [Fact]
    public async Task Unregister_RemovesTheServer()
    {
        this.ServeALease();

        await this.TestCommandAsync( $"license register {_url}" );
        await this.TestCommandAsync( "license unregister", expectedOutput: "All license keys and license servers have been unregistered." );

        await this.TestCommandAsync( "license list", expectedOutput: "No Metalama license is currently registered." );
    }
}
