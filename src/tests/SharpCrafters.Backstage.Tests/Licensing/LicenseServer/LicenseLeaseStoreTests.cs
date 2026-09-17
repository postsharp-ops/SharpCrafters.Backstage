// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Configuration;
using SharpCrafters.Backstage.Licensing;
using SharpCrafters.Backstage.Licensing.LicenseServer;
using System;
using System.Collections.Generic;
using System.Text.Json;
using Xunit;
using Xunit.Abstractions;

namespace SharpCrafters.Backstage.Tests.Licensing.LicenseServer;

/// <summary>
/// Tests where the product keeps what a license server has granted the user, so that their next build is licensed
/// by what the last one obtained instead of asking their organization for a second seat.
/// </summary>
public sealed class LicenseLeaseStoreTests : LicensingTestsBase
{
    private const string _url = "https://license.test";
    private const string _otherUrl = "https://other.license.test";

    private static readonly DateTime _start = new( 2026, 6, 1, 12, 0, 0, DateTimeKind.Utc );

    private LicenseLeaseStore _store = null!;

    public LicenseLeaseStoreTests( ITestOutputHelper logger ) : base( logger ) { }

    protected override void OnAfterServicesCreated( Services services )
    {
        base.OnAfterServicesCreated( services );

        this._store = new LicenseLeaseStore( services.ServiceProvider );
    }

    private LicenseLeaseStore Store
    {
        get
        {
            this.EnsureServicesInitialized();

            return this._store;
        }
    }

    private static LicenseLease CreateLease( string licenseKey = "KEY", int days = 3 )
        => new( licenseKey, _start, _start.AddDays( days ), _start.AddDays( days - 1 ) );

    /// <summary>
    /// Tests that a lease survives being stored and read back unchanged. This is what spares the customer a seat:
    /// the build that follows the one which leased reads the lease of its predecessor instead of asking the server
    /// for a second one.
    /// </summary>
    [Fact]
    public void LeaseRoundTrips()
    {
        var lease = CreateLease();
        this.Store.SetLease( _url, lease );

        Assert.True( this.Store.TryGetLease( _url, out var storedLease ) );
        Assert.Equal( lease, storedLease );
    }

    /// <summary>
    /// Tests that a lease means the same period wherever it is read. A developer travelling, or a machine set to a
    /// different time zone, must not find their licence expiring hours early or lasting hours too long.
    /// </summary>
    [Fact]
    public void StoredInstantsStayUtc()
    {
        this.Store.SetLease( _url, CreateLease() );

        Assert.True( this.Store.TryGetLease( _url, out var storedLease ) );
        Assert.Equal( DateTimeKind.Utc, storedLease.StartTime.Kind );
        Assert.Equal( DateTimeKind.Utc, storedLease.EndTime.Kind );
        Assert.Equal( DateTimeKind.Utc, storedLease.RenewTime.Kind );
    }

    /// <summary>
    /// Tests that a server the user has never leased from holds no lease, so that the first build against a freshly
    /// registered server acquires one rather than failing on an entry that was never written.
    /// </summary>
    [Fact]
    public void UnknownServerHasNoLease()
    {
        Assert.False( this.Store.TryGetLease( _url, out var lease ) );
        Assert.Null( lease );
    }

    /// <summary>
    /// Tests that a user who has never leased anything has no leases, rather than something the product cannot
    /// read. This is the state of every installation until the first build against a license server.
    /// </summary>
    [Fact]
    public void FreshConfigurationHasNoLeases()
    {
        var configuration = this.ConfigurationManager!.Get<LicenseServerConfiguration>();

        Assert.Empty( configuration.Leases );
    }

    /// <summary>
    /// Tests that two registrations that differ only by a trailing slash or by the case of the host share one lease.
    /// Otherwise the same server would be leased twice and would account two seats for one user.
    /// </summary>
    [Theory]
    [InlineData( "https://license.test", "https://license.test/" )]
    [InlineData( "https://license.test/", "https://license.test" )]
    [InlineData( "https://license.test", "HTTPS://LICENSE.TEST" )]
    [InlineData( "https://license.test/postsharp", "https://LICENSE.TEST/postsharp" )]
    public void EquivalentUrlsShareOneLease( string writeUrl, string readUrl )
    {
        this.Store.SetLease( writeUrl, CreateLease() );

        Assert.True( this.Store.TryGetLease( readUrl, out _ ) );
        Assert.Single( this.ConfigurationManager!.Get<LicenseServerConfiguration>().Leases );
    }

    /// <summary>
    /// Tests that renewing replaces the lease of a server instead of adding a second entry for it. A store that
    /// accumulated one entry per renewal would grow without end and would leave the product to guess which of them
    /// is current.
    /// </summary>
    /// <summary>
    /// Tests that two servers published under paths that differ only by case keep their own leases. They may be the
    /// servers of two divisions, with two pools of seats; a build licensed from the wrong one would draw a seat its
    /// team never bought.
    /// </summary>
    [Fact]
    public void ServersWhosePathsDifferByCaseKeepTheirOwnLeases()
    {
        this.Store.SetLease( "https://license.test/TeamA", CreateLease( "KEY-A" ) );
        this.Store.SetLease( "https://license.test/teama", CreateLease( "KEY-B" ) );

        Assert.True( this.Store.TryGetLease( "https://license.test/TeamA", out var first ) );
        Assert.Equal( "KEY-A", first.LicenseKey );

        Assert.True( this.Store.TryGetLease( "https://license.test/teama", out var second ) );
        Assert.Equal( "KEY-B", second.LicenseKey );
    }

    [Fact]
    public void SetLeaseReplacesThePreviousOne()
    {
        this.Store.SetLease( _url, CreateLease( "FIRST" ) );
        this.Store.SetLease( _url, CreateLease( "SECOND" ) );

        Assert.True( this.Store.TryGetLease( _url, out var lease ) );
        Assert.Equal( "SECOND", lease.LicenseKey );
        Assert.Single( this.ConfigurationManager!.Get<LicenseServerConfiguration>().Leases );
    }

    /// <summary>
    /// Tests that the leases of two license servers do not interfere. A customer may lease from more than one
    /// server, and forgetting one must not take the licence of the other away.
    /// </summary>
    [Fact]
    public void LeasesOfTwoServersAreIndependent()
    {
        this.Store.SetLease( _url, CreateLease( "FIRST" ) );
        this.Store.SetLease( _otherUrl, CreateLease( "SECOND" ) );

        this.Store.RemoveLease( _url );

        Assert.False( this.Store.TryGetLease( _url, out _ ) );
        Assert.True( this.Store.TryGetLease( _otherUrl, out var lease ) );
        Assert.Equal( "SECOND", lease.LicenseKey );
    }

    /// <summary>
    /// Tests that forgetting a lease the user never held leaves the leases they do hold alone, so that
    /// unregistering a server they mistyped does not unlicense the builds that were working.
    /// </summary>
    [Fact]
    public void RemoveLeaseOfUnknownServerDoesNothing()
    {
        this.Store.SetLease( _url, CreateLease() );

        this.Store.RemoveLease( _otherUrl );

        Assert.True( this.Store.TryGetLease( _url, out _ ) );
    }

    /// <summary>
    /// Tests that unregistering really forgets every lease. A user who unregisters expects the product to stop
    /// using what it was leasing, not to go on using it until the lease runs out days later.
    /// </summary>
    [Fact]
    public void RemoveAllLeasesClearsEveryServer()
    {
        this.Store.SetLease( _url, CreateLease() );
        this.Store.SetLease( _otherUrl, CreateLease() );

        this.Store.RemoveAllLeases();

        Assert.False( this.Store.TryGetLease( _url, out _ ) );
        Assert.False( this.Store.TryGetLease( _otherUrl, out _ ) );
        Assert.Empty( this.ConfigurationManager!.Get<LicenseServerConfiguration>().Leases );
    }

    /// <summary>
    /// Tests that an entry whose licence key is blank is read as no lease at all, so that a file edited by hand or
    /// written by a version that stored an empty placeholder does not produce a licence with no key.
    /// </summary>
    [Fact]
    public void EntryWithoutLicenseKeyIsIgnored()
    {
        var configuration = this.ConfigurationManager!.Get<LicenseServerConfiguration>();

        this.ConfigurationManager!.Set(
            configuration with { Leases = configuration.Leases.SetItem( _url, new LeaseConfiguration { LicenseKey = "" } ) } );

        Assert.False( this.Store.TryGetLease( _url, out _ ) );
    }

    /// <summary>
    /// Tests that a member of a lease that a later version of the product wrote survives a renewal by this one.
    /// Several versions share the configuration files of the user profile, so a version that dropped the members it
    /// does not declare would destroy what a later one stored (#1923).
    /// </summary>
    [Fact]
    public void UnknownMembersOfALeaseSurviveARenewal()
    {
        this.Store.SetLease( _url, CreateLease( "FIRST" ) );

        // Stand in for a later version that added a member to the entry of this server.
        var configuration = this.ConfigurationManager!.Get<LicenseServerConfiguration>();
        var lease = configuration.Leases[_url];

        lease.UnknownMembers = new Dictionary<string, JsonElement>
        {
            ["seatToken"] = JsonDocument.Parse( "\"abc\"" ).RootElement
        };

        this.ConfigurationManager!.Set( configuration with { Leases = configuration.Leases.SetItem( _url, lease ) } );

        this.Store.SetLease( _url, CreateLease( "SECOND" ) );

        var renewedLease = this.ConfigurationManager!.Get<LicenseServerConfiguration>().Leases[_url];

        Assert.Equal( "SECOND", renewedLease.LicenseKey );
        Assert.NotNull( renewedLease.UnknownMembers );
        Assert.Equal( "abc", renewedLease.UnknownMembers!["seatToken"].GetString() );
    }

    /// <summary>
    /// Tests that storing a lease from inside the transformation of another configuration file is skipped rather than
    /// raising. A lease is written as a side effect of consuming a licence, so the write can be reached from a handler
    /// that itself runs inside an update; updating a second file from there would make the thread hold two of the
    /// locks that protect the files. Skipping costs one request to the server later, whereas raising would fail the
    /// operation that merely happened to trigger it.
    /// </summary>
    [Fact]
    public void StoringFromWithinAnotherUpdateIsSkipped()
    {
        this.ConfigurationManager!.Update<LicensingConfiguration>(
            configuration =>
            {
                this.Store.SetLease( _url, CreateLease() );

                return configuration;
            } );

        Assert.False( this.Store.TryGetLease( _url, out _ ) );
    }

    /// <summary>
    /// Tests that the lease really is stored in the ordinary case. Its companion shows the one case where the
    /// product gives up on storing it; without this, that test would pass even if the product never stored a lease
    /// at all.
    /// </summary>
    [Fact]
    public void StoringOutsideAnotherUpdateSucceeds()
    {
        this.ConfigurationManager!.Update<LicensingConfiguration>( configuration => configuration );

        this.Store.SetLease( _url, CreateLease() );

        Assert.True( this.Store.TryGetLease( _url, out _ ) );
    }
}
