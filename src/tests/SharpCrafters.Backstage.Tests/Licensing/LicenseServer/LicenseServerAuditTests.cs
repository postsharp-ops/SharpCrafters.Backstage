// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Extensibility;
using SharpCrafters.Backstage.Licensing.Audit;
using SharpCrafters.Backstage.Licensing.Consumption;
using SharpCrafters.Backstage.Telemetry;
using SharpCrafters.Backstage.Testing;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace SharpCrafters.Backstage.Tests.Licensing.LicenseServer;

/// <summary>
/// Tests that a licence leased from a license server is accounted for by the licence audit, like any other.
/// </summary>
/// <remarks>
/// A licence leased from a server is accounted for twice: by the audit, as every licence key is, and in the ledger
/// of the server, which is the copy the customer reads. The two answer different questions — what we are told the
/// customer used, and what the customer can see for themselves — and a leased licence that reached neither would be
/// the one kind of licence nobody could account for.
/// </remarks>
public sealed class LicenseServerAuditTests : LicenseServerTestsBase
{
    public LicenseServerAuditTests( ITestOutputHelper logger ) : base( logger )
    {
        // The audit travels with the telemetry of the product, and the test application has to allow it before the
        // services are built.
        this.ApplicationInfo = new TestApplicationInfo(
            "License Server Test App",
            false,
            TestApplicationInfo.CurrentPackageVersion,
            new DateTime( 2026, 1, 15, 0, 0, 0, DateTimeKind.Utc ) ) { IsTelemetryEnabled = true };
    }

    protected override void ConfigureServices( ServiceProviderBuilder services )
    {
        base.ConfigureServices( services );

        services.AddService( typeof(TelemetryLogger), serviceProvider => new TelemetryLogger( serviceProvider ) );
        services.AddService( typeof(ITelemetryUploader), new NullTelemetryUploader() );
        services.AddService( typeof(IUsageSessionFactory), new NullUsageSessionFactory() );
        services.AddService( typeof(ILicenseAuditManager), serviceProvider => new LicenseAuditManager( serviceProvider ) );
    }

    /// <summary>
    /// Gets what the audit has written so far, after the background work that writes it has finished.
    /// </summary>
    private string[] GetAuditReports()
    {
        this.BackgroundTasks.WhenNoPendingTaskAsync().Wait();

        return this.FileSystem.Mock.AllFiles
            .Where( path => Path.GetFileName( path ).StartsWith( "LicenseAudit-", StringComparison.Ordinal ) )
            .SelectMany( this.FileSystem.ReadAllLines )
            .ToArray();
    }

    /// <summary>
    /// Tests that consuming a licence leased from a license server is reported to the audit, and that what is
    /// reported is the licence key the server granted — the thing that was actually used, rather than the address it
    /// came from.
    /// </summary>
    [Fact]
    public async Task LeasedLicenseIsAudited()
    {
        var server = this.CreateServer();

        await this.LicenseRegistrationService.RegisterLicenseAsync( server.Url );

        var consumer = await this.ServiceProvider.GetRequiredBackstageService<ILicenseConsumptionService>()
            .CreateConsumerAsync( null, this.Messages.Add );

        Assert.True( consumer.TryConsume( LicenseRequirement.Any, this.Messages.Add ) );

        var report = Assert.Single( this.GetAuditReports() );
        Assert.Contains( server.LicenseKey, report, StringComparison.OrdinalIgnoreCase );
    }

    /// <summary>
    /// Tests that a licence the build never used is not audited. The audit records what a customer consumed; a server
    /// that was registered and a lease that was acquired are not, on their own, a use of the product.
    /// </summary>
    [Fact]
    public async Task RegisteringAServerAuditsNothing()
    {
        var server = this.CreateServer();

        await this.LicenseRegistrationService.RegisterLicenseAsync( server.Url );

        Assert.Empty( this.GetAuditReports() );
    }
}
