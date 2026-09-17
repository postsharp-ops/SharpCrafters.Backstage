// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Extensibility;
using SharpCrafters.Backstage.Licensing.Consumption;
using SharpCrafters.Backstage.Licensing.LicenseServer;
using SharpCrafters.Backstage.Testing;
using System;
using System.Collections.Generic;
using Xunit.Abstractions;

namespace SharpCrafters.Backstage.Tests.Licensing.LicenseServer;

/// <summary>
/// The base of the tests that exercise a license server end to end.
/// </summary>
/// <remarks>
/// <para>
/// The application declares the version of the product itself, which is at or above
/// <see cref="SharpCrafters.Backstage.Licensing.Registration.LicensingConstants.MinimalLicenseServerVersion"/>:
/// a registered license server URL is stored in the group of that version, and an application older than it would
/// skip the group. That is also why the version is read from the assembly rather than written here -- a literal
/// would stop being the version of the product without anything saying so. <see cref="LicensingTestsBase"/> pins
/// version 1.0, which is what the tests of the license key groups depend on, so it is not raised there.
/// </para>
/// <para>
/// The build date stays a literal, because it is matched against the subscription period of the licence keys that
/// these tests use, and those periods are fixed.
/// </para>
/// </remarks>
public abstract class LicenseServerTestsBase : LicensingTestsBase
{
    /// <summary>
    /// The instant at which every test of this family starts, so that a lease boundary is arithmetic rather than a
    /// reading of the wall clock.
    /// </summary>
    protected static readonly DateTime StartTime = new( 2026, 6, 1, 12, 0, 0, DateTimeKind.Utc );

    private protected LicenseServerTestsBase( ITestOutputHelper logger ) : base( logger )
    {
        this.ApplicationInfo = new TestApplicationInfo(
            "License Server Test App",
            false,
            TestApplicationInfo.CurrentPackageVersion,
            new DateTime( 2026, 1, 15, 0, 0, 0, DateTimeKind.Utc ) );

        this.Time.Set( StartTime, false );
        this.UserDeviceDetection.IsInteractiveDevice = true;
        this.UserIdentity.UserName = "testuser";
        this.UserIdentity.MachineName = "TEST-MACHINE";
    }

    /// <summary>
    /// Gets the licensing messages that the code under test reported.
    /// </summary>
    protected List<LicensingMessage> Messages { get; } = [];

    /// <summary>
    /// Gets the store of the leases of the current test, which is where a lease survives between two runs.
    /// </summary>
    private protected LicenseLeaseStore LeaseStore => this.ServiceProvider.GetRequiredBackstageService<LicenseLeaseStore>();

    /// <summary>
    /// Creates a license server simulator bound to the HTTP client factory and the clock of the current test, leasing
    /// a licence key that a server is allowed to lease.
    /// </summary>
    protected LicenseServerSimulator CreateServer( string url = LicenseServerSimulator.DefaultUrl )
    {
        this.EnsureServicesInitialized();

        return new LicenseServerSimulator( this.HttpClientFactory, this.Time, url )
        {
            LicenseKey = LicenseKeyProvider.MetalamaEnterpriseLicenseServerEligible
        };
    }
}
