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
/// Tests the store that keeps the lease held from each license server between two runs of the product.
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

    [Fact]
    public void LeaseRoundTrips()
    {
        var lease = CreateLease();
        this.Store.SetLease( _url, lease );

        Assert.True( this.Store.TryGetLease( _url, out var storedLease ) );
        Assert.Equal( lease, storedLease );
    }

    /// <summary>
    /// Tests that the instants keep their UTC kind through the store, so that a lease written in one time zone is not
    /// read as a different instant in another.
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

    [Fact]
    public void UnknownServerHasNoLease()
    {
        Assert.False( this.Store.TryGetLease( _url, out var lease ) );
        Assert.Null( lease );
    }

    /// <summary>
    /// Tests that a fresh configuration has an empty, and not a null or default, set of leases. A default
    /// <see cref="System.Collections.Immutable.ImmutableDictionary{TKey,TValue}"/> throws when it is enumerated.
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
    public void EquivalentUrlsShareOneLease( string writeUrl, string readUrl )
    {
        this.Store.SetLease( writeUrl, CreateLease() );

        Assert.True( this.Store.TryGetLease( readUrl, out _ ) );
        Assert.Single( this.ConfigurationManager!.Get<LicenseServerConfiguration>().Leases );
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

    [Fact]
    public void RemoveLeaseOfUnknownServerDoesNothing()
    {
        this.Store.SetLease( _url, CreateLease() );

        this.Store.RemoveLease( _otherUrl );

        Assert.True( this.Store.TryGetLease( _url, out _ ) );
    }

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
    /// Tests that the guard of the previous test really is the reason nothing was written, by verifying that the same
    /// call outside a transformation does store the lease.
    /// </summary>
    [Fact]
    public void StoringOutsideAnotherUpdateSucceeds()
    {
        this.ConfigurationManager!.Update<LicensingConfiguration>( configuration => configuration );

        this.Store.SetLease( _url, CreateLease() );

        Assert.True( this.Store.TryGetLease( _url, out _ ) );
    }
}
