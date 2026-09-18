// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using PostSharp.Backstage.Configuration;
using SharpCrafters.Backstage.Configuration;
using SharpCrafters.Backstage.Configuration.Registry;
using SharpCrafters.Backstage.Extensibility;
using SharpCrafters.Backstage.Licensing.LicenseServer;
using SharpCrafters.Backstage.Testing;
using System;
using System.Collections.Immutable;
using Xunit;
using Xunit.Abstractions;

namespace SharpCrafters.Backstage.Tests.Registry;

/// <summary>
/// Tests that a lease held from a license server is cached where PostSharp 2026.0 caches it.
/// </summary>
/// <remarks>
/// This is the one shared setting whose sharing the user can be charged for: a license server allocates a seat per
/// lease, and a developer who builds with both versions would otherwise take two.
/// </remarks>
public sealed class PostSharpLicenseServerConfigurationSchemaTests : TestsBase
{
    private const string _leasedLicensesKeyPath = @"Software\SharpCrafters\PostSharp 3\LeasedLicenses";
    private const string _serverUrl = "https://licenses.example.com/postsharp";

    private readonly TestRegistryService _registry = new();

    public PostSharpLicenseServerConfigurationSchemaTests( ITestOutputHelper logger ) : base( logger ) { }

    protected override void ConfigureServices( ServiceProviderBuilder services )
    {
        base.ConfigureServices( services );
        services.AddService( typeof(IRegistryService), this._registry );
    }

    private RegistryConfigurationManager CreateManager()
        => new(
            this.ServiceProvider,
            new InMemoryConfigurationManager( this.ServiceProvider ),
            [new PostSharpLicenseServerConfigurationSchema( this.Time )] );

    private IRegistryKey ServerKey( string url = _serverUrl )
        => this._registry.GetOrCreateKey( RegistryHiveKind.CurrentUser, _leasedLicensesKeyPath ).CreateSubKey( url )!;

    private LicenseServerConfiguration Read()
    {
        using var manager = this.CreateManager();

        return manager.Get<LicenseServerConfiguration>();
    }

    private void Update( Func<LicenseServerConfiguration, LicenseServerConfiguration> transform )
    {
        using var manager = this.CreateManager();
        manager.Update( typeof(LicenseServerConfiguration), current => transform( (LicenseServerConfiguration) current ) );
    }

    private static LeaseConfiguration ALease( string licenseKey = "a-leased-key" )
        => new()
        {
            LicenseKey = licenseKey,
            StartTime = new DateTime( 2026, 9, 1, 8, 0, 0, DateTimeKind.Utc ),
            EndTime = new DateTime( 2026, 10, 1, 8, 0, 0, DateTimeKind.Utc ),
            RenewTime = new DateTime( 2026, 9, 20, 8, 0, 0, DateTimeKind.Utc )
        };

    /// <summary>
    /// A lease that PostSharp 2026.0 cached is read from the default value of the key named after the server, in
    /// the textual form that version writes.
    /// </summary>
    [Fact]
    public void ALeaseCachedByTheOtherVersionIsRead()
    {
        this.ServerKey()
            .SetStringValue(
                "",
                "License: a-leased-key; StartTime: 2026-09-01T08:00:00.0000000Z; EndTime: 2026-10-01T08:00:00.0000000Z; RenewTime: 2026-09-20T08:00:00.0000000Z" );

        var lease = this.Read().Leases[_serverUrl];

        Assert.Equal( "a-leased-key", lease.LicenseKey );
        Assert.Equal( new DateTime( 2026, 9, 1, 8, 0, 0, DateTimeKind.Utc ), lease.StartTime.ToUniversalTime() );
        Assert.Equal( new DateTime( 2026, 10, 1, 8, 0, 0, DateTimeKind.Utc ), lease.EndTime.ToUniversalTime() );
        Assert.Equal( new DateTime( 2026, 9, 20, 8, 0, 0, DateTimeKind.Utc ), lease.RenewTime.ToUniversalTime() );
    }

    /// <summary>
    /// A lease acquired here is written in the same form, so that the other version finds it rather than taking a
    /// second seat.
    /// </summary>
    /// <remarks>
    /// The instants carry no fractional part, because that is how PostSharp 2026.0 writes them: it formats them with
    /// <c>XmlConvert</c>. Writing the round-trip form instead would give the same instant in a different spelling, and
    /// the value would then differ from the one that version wrote and be rewritten on every read of an unchanged
    /// configuration.
    /// </remarks>
    [Fact]
    public void ALeaseAcquiredHereIsWrittenInTheFormTheOtherVersionReads()
    {
        this.Update( c => c with { Leases = c.Leases.Add( _serverUrl, ALease() ) } );

        var stored = (string) this.ServerKey().GetValue( "" )!;

        Assert.Equal(
            "License: a-leased-key; StartTime: 2026-09-01T08:00:00Z; EndTime: 2026-10-01T08:00:00Z; RenewTime: 2026-09-20T08:00:00Z",
            stored );
    }

    [Fact]
    public void ALeaseSurvivesARoundTrip()
    {
        this.Update( c => c with { Leases = c.Leases.Add( _serverUrl, ALease() ) } );

        Assert.Equal( ALease(), this.Read().Leases[_serverUrl] );
    }

    /// <summary>
    /// A blank value is what the other version leaves behind when it finds a lease that has expired. It is not a
    /// lease and it is not an error.
    /// </summary>
    [Fact]
    public void ABlankValueIsNoLease()
    {
        this.ServerKey().SetStringValue( "", "" );

        Assert.Empty( this.Read().Leases );
    }

    /// <summary>
    /// A value that does not parse is no lease either. A lease is derived state that can be acquired again, so the
    /// worst a bad value costs is one request.
    /// </summary>
    [Fact]
    public void AValueThatDoesNotParseIsNoLease()
    {
        this.ServerKey().SetStringValue( "", "this is not a lease" );

        Assert.Empty( this.Read().Leases );
    }

    /// <summary>
    /// Releasing a lease blanks the value rather than deleting the key of the server, which is what the other
    /// version does to a lease that has expired, and which leaves the values it keeps beside this one alone.
    /// </summary>
    [Fact]
    public void ReleasingALeaseBlanksTheValue()
    {
        this.Update( c => c with { Leases = c.Leases.Add( _serverUrl, ALease() ) } );

        // A value that the other version keeps for a license singled out by its version.
        this.ServerKey().SetStringValue( "6.9.3", "License: another-key; StartTime: 2026-09-01T08:00:00.0000000Z; EndTime: 2026-10-01T08:00:00.0000000Z; RenewTime: 2026-09-20T08:00:00.0000000Z" );

        this.Update( c => c with { Leases = ImmutableDictionary<string, LeaseConfiguration>.Empty } );

        Assert.Equal( "", this.ServerKey().GetValue( "" ) );
        Assert.True( this._registry.KeyExists( RegistryHiveKind.CurrentUser, _leasedLicensesKeyPath ) );
        Assert.NotEqual( "", this.ServerKey().GetValue( "6.9.3" ) );
        Assert.Empty( this.Read().Leases );
    }

    /// <summary>
    /// Two spellings of one address are one server. The key is named with whatever spelling was registered, and the
    /// entry is keyed the way the rest of this product keys a server.
    /// </summary>
    [Fact]
    public void TwoSpellingsOfOneAddressAreOneServer()
    {
        this.ServerKey( "https://Licenses.Example.com/postsharp" )
            .SetStringValue(
                "",
                "License: a-leased-key; StartTime: 2026-09-01T08:00:00.0000000Z; EndTime: 2026-10-01T08:00:00.0000000Z; RenewTime: 2026-09-20T08:00:00.0000000Z" );

        var leases = this.Read().Leases;

        Assert.Single( leases );
        Assert.True( leases.ContainsKey( _serverUrl ) );
    }

    /// <summary>
    /// Several servers are several keys.
    /// </summary>
    [Fact]
    public void EachServerHasItsOwnKey()
    {
        const string otherServerUrl = "https://licenses.example.com/other";

        this.Update(
            c => c with { Leases = c.Leases.Add( _serverUrl, ALease() ).Add( otherServerUrl, ALease( "another-leased-key" ) ) } );

        Assert.Equal( "a-leased-key", this.Read().Leases[_serverUrl].LicenseKey );
        Assert.Equal( "another-leased-key", this.Read().Leases[otherServerUrl].LicenseKey );
    }
}
