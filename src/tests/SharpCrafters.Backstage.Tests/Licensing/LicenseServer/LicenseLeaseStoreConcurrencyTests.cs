// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using Microsoft.Extensions.DependencyInjection;
using SharpCrafters.Backstage.Configuration;
using SharpCrafters.Backstage.Extensibility;
using SharpCrafters.Backstage.Licensing.LicenseServer;
using System;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace SharpCrafters.Backstage.Tests.Licensing.LicenseServer;

/// <summary>
/// Tests what happens to the leases of a user when several builds renew at once.
/// </summary>
/// <remarks>
/// <para>
/// A developer builds several projects at once, and every one of those processes reads and writes the same file in
/// their profile. A lease that is lost in that race is a seat the customer paid for and does not get: the build that
/// lost it asks the server for another one, on a machine that already held it.
/// </para>
/// <para>
/// These tests use the real <see cref="Configuration.ConfigurationManager"/> over the in-memory file system, and two
/// service providers over one file system, which is as close to two processes as a test can get without starting
/// one. The in-memory configuration manager that the other tests use holds its state per provider and would report
/// that everything is fine.
/// </para>
/// </remarks>
public sealed class LicenseLeaseStoreConcurrencyTests : LicensingTestsBase
{
    private const int _leaseCount = 24;

    private static readonly DateTime _start = new( 2026, 6, 1, 12, 0, 0, DateTimeKind.Utc );

    public LicenseLeaseStoreConcurrencyTests( ITestOutputHelper logger ) : base( logger ) { }

    /// <summary>
    /// Replaces the in-memory configuration manager of the test base with the real one, which is what holds the lock
    /// that these tests are about.
    /// </summary>
    protected override void ConfigureServices( ServiceProviderBuilder services )
    {
        base.ConfigureServices( services );

        services.AddService( typeof(IConfigurationManager), serviceProvider => new Configuration.ConfigurationManager( serviceProvider ) );
    }

    private static LicenseLease CreateLease( string licenseKey )
        => new( licenseKey, _start, _start.AddDays( 3 ), _start.AddDays( 2 ) );

    private static string UrlOf( int index ) => $"https://license{index}.test";

    /// <summary>
    /// Builds a second installation over the same profile: another service provider, another configuration manager,
    /// the same files.
    /// </summary>
    private LicenseLeaseStore CreateStoreOfAnotherProcess()
    {
        this.EnsureServicesInitialized();

        return new LicenseLeaseStore( this.CloneServiceCollection().BuildServiceProvider() );
    }

    /// <summary>
    /// Tests that no lease is lost when two processes store leases of different servers at the same time. Each one
    /// rewrites the whole file, so a write that started from what it read before the other had finished would drop
    /// the lease of the other, and a customer would pay a second seat for a machine that already had one.
    /// </summary>
    [Fact]
    public void NoLeaseIsLostWhenTwoProcessesWriteAtOnce()
    {
        var thisProcess = new LicenseLeaseStore( this.ServiceProvider );
        var otherProcess = this.CreateStoreOfAnotherProcess();

        Parallel.For( 0, _leaseCount, i => (i % 2 == 0 ? thisProcess : otherProcess).SetLease( UrlOf( i ), CreateLease( $"KEY-{i}" ) ) );

        // Read from an installation that took part in neither write, so that the assertion is about what is on disk
        // and not about what either writer remembers.
        var reader = this.CreateStoreOfAnotherProcess();

        for ( var i = 0; i < _leaseCount; i++ )
        {
            Assert.True( reader.TryGetLease( UrlOf( i ), out var lease ), $"The lease of {UrlOf( i )} was lost." );
            Assert.Equal( $"KEY-{i}", lease.LicenseKey );
        }
    }

    /// <summary>
    /// Tests that two processes renewing the lease of the same server leave one lease and not two. A second entry for
    /// one server would make the product hold two leases of one machine, and the customer would see a seat they
    /// cannot account for.
    /// </summary>
    [Fact]
    public void TwoProcessesRenewingOneServerLeaveOneLease()
    {
        var thisProcess = new LicenseLeaseStore( this.ServiceProvider );
        var otherProcess = this.CreateStoreOfAnotherProcess();

        Parallel.For( 0, _leaseCount, i => (i % 2 == 0 ? thisProcess : otherProcess).SetLease( UrlOf( 0 ), CreateLease( $"KEY-{i}" ) ) );

        var configuration = this.CloneServiceCollection()
            .BuildServiceProvider()
            .GetRequiredBackstageService<IConfigurationManager>()
            .Get<LicenseServerConfiguration>();

        var lease = Assert.Single( configuration.Leases );
        Assert.Equal( LicenseServerUrl.GetStoreKey( UrlOf( 0 ) ), lease.Key );
        Assert.StartsWith( "KEY-", lease.Value.LicenseKey, StringComparison.Ordinal );
    }

    /// <summary>
    /// Tests that a lease a process stores is there for the next one to read. This is the whole reason the store
    /// exists, and the two tests above would pass on a store that wrote nothing at all.
    /// </summary>
    [Fact]
    public void ALeaseStoredByOneProcessIsReadByTheNext()
    {
        new LicenseLeaseStore( this.ServiceProvider ).SetLease( UrlOf( 0 ), CreateLease( "KEY" ) );

        Assert.True( this.CreateStoreOfAnotherProcess().TryGetLease( UrlOf( 0 ), out var lease ) );
        Assert.Equal( "KEY", lease.LicenseKey );
    }
}
