// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using PostSharp.Backstage;
using PostSharp.Backstage.Configuration;
using PostSharp.Backstage.Licensing;
using SharpCrafters.Backstage.Configuration;
using SharpCrafters.Backstage.Configuration.Registry;
using SharpCrafters.Backstage.Extensibility;
using SharpCrafters.Backstage.Licensing;
using SharpCrafters.Backstage.Licensing.Audit;
using SharpCrafters.Backstage.Licensing.Consumption;
using SharpCrafters.Backstage.Licensing.Licenses;
using SharpCrafters.Backstage.Testing;
using System;
using System.Globalization;
using Xunit;
using Xunit.Abstractions;

namespace SharpCrafters.Backstage.Tests.Registry;

/// <summary>
/// Tests that the record of the license audits is shared with PostSharp 2026.0, and that it is keyed the way that
/// version keys it.
/// </summary>
/// <remarks>
/// The two go together. Sharing the key without sharing the naming would be worse than not sharing at all: the two
/// versions would write into one key and neither would read what the other wrote, so each would audit every license
/// the other had just audited, and the record would grow with two entries per license instead of one.
/// </remarks>
public sealed class PostSharpLicenseAuditConfigurationSchemaTests : TestsBase
{
    private const string _licenseAuditKeyPath = @"Software\SharpCrafters\PostSharp 3\LicenseAudit";

    private readonly TestRegistryService _registry = new();

    public PostSharpLicenseAuditConfigurationSchemaTests( ITestOutputHelper logger ) : base( logger ) { }

    protected override void ConfigureServices( ServiceProviderBuilder services )
    {
        base.ConfigureServices( services );
        services.AddService( typeof(IRegistryService), this._registry );
    }

    private RegistryConfigurationManager CreateManager()
        => new(
            this.ServiceProvider,
            new InMemoryConfigurationManager( this.ServiceProvider ),
            [new PostSharpLicenseAuditConfigurationSchema()] );

    private IRegistryKey AuditKey() => this._registry.GetOrCreateKey( RegistryHiveKind.CurrentUser, _licenseAuditKeyPath );

    private LicenseAuditConfiguration Read()
    {
        using var manager = this.CreateManager();

        return manager.Get<LicenseAuditConfiguration>();
    }

    private void Update( Func<LicenseAuditConfiguration, LicenseAuditConfiguration> transform )
    {
        using var manager = this.CreateManager();
        manager.Update( typeof(LicenseAuditConfiguration), current => transform( (LicenseAuditConfiguration) current ) );
    }

    /// <summary>
    /// Builds the properties that reach a consumer, which is what the key provider is given.
    /// </summary>
    private static LicenseConsumptionProperties ALicense( string? licenseString )
        => new(
            LicenseProduct.PostSharpUltimate,
            LicenseType.Business,
            null,
            "PostSharp Ultimate",
            new Version( 1, 0 ),
            licenseString,
            false,
            true,
            null,
            null,
            SubscriptionStatus.None,
            LicenseGeneration.Current,
            ServicingPhase.Current );

    /// <summary>
    /// PostSharp keys the record by the identity of the license, which is the GUID of the key when it has one. This
    /// is what PostSharp 2026.0 names the value with, and it is the whole reason the record can be shared.
    /// </summary>
    [Fact]
    public void AnAuditIsKeyedByTheIdentityOfTheLicense()
    {
        var licenseKey = new TestLicenseKeyProvider().PostSharpUltimate;

        Assert.True( LicenseKeyData.TryDeserialize( licenseKey, out var licenseKeyData, out _ ) );

        // Text, so that it is filed among the licences and not among the hashes of report content, whatever the
        // identity happens to look like.
        Assert.Equal(
            LicenseAuditKey.FromText( licenseKeyData.LicenseUniqueId ),
            PostSharpLicenseAuditKeyProvider.Instance.GetAuditKey( ALicense( licenseKey ), 1234 ) );
    }

    /// <summary>
    /// A licence whose key carries no globally unique identifier is identified by its number, and that number is
    /// still a licence: it is given as text, so nothing can read it as a hash of report content.
    /// </summary>
    [Fact]
    public void ALicenceIdentifiedByItsNumberIsStillALicence()
    {
        var licenseKey = new TestLicenseKeyProvider().PostSharpThreading;

        Assert.True( LicenseKeyData.TryDeserialize( licenseKey, out var licenseKeyData, out _ ) );
        Assert.Null( licenseKeyData.LicenseGuid );

        var auditKey = PostSharpLicenseAuditKeyProvider.Instance.GetAuditKey( ALicense( licenseKey ), 1234 );

        Assert.Equal( LicenseAuditKey.FromText( licenseKeyData.LicenseId.ToString( CultureInfo.InvariantCulture ) ), auditKey );
        Assert.IsType<string>( auditKey.Value );
    }

    /// <summary>
    /// A license that is not a key has no identity of its own — the one an unattended build is given, for
    /// instance — and is throttled by the content of its report, as every other product throttles it.
    /// </summary>
    [Fact]
    public void ALicenseThatIsNotAKeyIsKeyedByItsReport()
    {
        Assert.Equal( LicenseAuditKey.FromNumber( 1234 ), PostSharpLicenseAuditKeyProvider.Instance.GetAuditKey( ALicense( null ), 1234 ) );
        Assert.Equal( LicenseAuditKey.FromNumber( 1234 ), PostSharpLicenseAuditKeyProvider.Instance.GetAuditKey( ALicense( "not-a-license-key" ), 1234 ) );
    }

    /// <summary>
    /// Metalama keeps throttling by the content of the report, which is what it has always done and what a product
    /// that shares the record with nothing should do.
    /// </summary>
    [Fact]
    public void TheDefaultThrottlesByTheReport()
        => Assert.Equal( LicenseAuditKey.FromNumber( -42 ), ReportContentLicenseAuditKeyProvider.Instance.GetAuditKey( ALicense( null ), -42 ) );

    /// <summary>
    /// A license that PostSharp 2026.0 has audited today is not audited again by this version.
    /// </summary>
    [Fact]
    public void AnAuditOfTheOtherVersionIsRead()
    {
        var auditTime = new DateTime( 2026, 9, 18, 9, 0, 0, DateTimeKind.Utc );
        this.AuditKey().SetQWordValue( "d3cf9b1e-6b17-4b0b-9f1c-0f3b9d0a1e2f", RegistryValueConverters.DateTimeToQWord( auditTime ) );

        Assert.True( this.Read().TryGetLastAuditTime( LicenseAuditKey.FromText( "d3cf9b1e-6b17-4b0b-9f1c-0f3b9d0a1e2f" ), out var lastAuditTime ) );
        Assert.Equal( auditTime, lastAuditTime.ToUniversalTime() );
    }

    /// <summary>
    /// An audit made here is written where the other version looks, as a date and not as anything else: that version
    /// reads the value as a number of milliseconds.
    /// </summary>
    [Fact]
    public void AnAuditMadeHereIsWrittenWhereTheOtherVersionLooks()
    {
        var auditTime = new DateTime( 2026, 9, 18, 9, 0, 0, DateTimeKind.Utc );

        this.Update( c => c.SetLastAuditTime( LicenseAuditKey.FromText( "a-license-identity" ), auditTime ) );

        var stored = this.AuditKey().GetValue( "a-license-identity" );

        Assert.IsType<long>( stored );
        Assert.Equal( auditTime, RegistryValueConverters.QWordToDateTime( stored )!.Value.ToUniversalTime() );
    }

    /// <summary>
    /// An entry that this version does not hold is left alone, unlike the entries of the dictionaries it owns. The
    /// record is shared, and an entry missing here may be one the other version has just written; removing it would
    /// make that version audit the license again.
    /// </summary>
    [Fact]
    public void AnEntryOfTheOtherVersionIsNotCollected()
    {
        this.AuditKey().SetQWordValue( "written-by-the-other-version", RegistryValueConverters.DateTimeToQWord( DateTime.UtcNow ) );

        this.Update( c => c.SetLastAuditTime( LicenseAuditKey.FromText( "ours" ), DateTime.UtcNow ) );

        Assert.NotNull( this.AuditKey().GetValue( "written-by-the-other-version" ) );
    }

    /// <summary>
    /// A licence that the other version identifies by its number is still a licence, so it is read as one and not as
    /// a hash of report content.
    /// </summary>
    /// <remarks>
    /// <para>
    /// PostSharp 2026.0 names a value after the identity of the licence, which is its number when the licence key
    /// carries no globally unique identifier. Such a name looks exactly like the numbers of the other record, which
    /// are hashes of the content of a report, and means something entirely different.
    /// </para>
    /// <para>
    /// This was read as a hash until the identity was made to say which record it belongs to. On a real machine the
    /// two were already mixed: the record there holds sixty-eight identifiers and three numbers, and the three are
    /// licences.
    /// </para>
    /// </remarks>
    [Fact]
    public void ANumericIdentityOfTheOtherVersionIsALicenceAndNotAHash()
    {
        var auditTime = new DateTime( 2026, 9, 18, 9, 0, 0, DateTimeKind.Utc );
        this.AuditKey().SetQWordValue( "22", RegistryValueConverters.DateTimeToQWord( auditTime ) );

        var configuration = this.Read();

        Assert.Equal( auditTime, configuration.LastAuditTimesByString!["22"].ToUniversalTime() );
        Assert.Empty( configuration.LastAuditTimesByLong );

        // And it answers for the licence, not for a report that hashes to the same number.
        Assert.True( configuration.TryGetLastAuditTime( LicenseAuditKey.FromText( "22" ), out _ ) );
        Assert.False( configuration.TryGetLastAuditTime( LicenseAuditKey.FromNumber( 22 ), out _ ) );
    }

    /// <summary>
    /// A registry key holds one namespace, so a licence identified by a number and a report hash of the same number
    /// would be one value. This product never files a report hash, so the case cannot arise; a number that reached
    /// the key before that was so is carried over to the record it belongs in rather than dropped.
    /// </summary>
    [Fact]
    public void ANumberOfTheOtherRecordIsCarriedOver()
    {
        var auditTime = new DateTime( 2026, 9, 18, 9, 0, 0, DateTimeKind.Utc );

        this.Update( c => c.SetLastAuditTime( LicenseAuditKey.FromNumber( 4242 ), auditTime ) );

        var configuration = this.Read();

        Assert.Equal( auditTime, configuration.LastAuditTimesByString!["4242"].ToUniversalTime() );
        Assert.Empty( configuration.LastAuditTimesByLong );
    }

    /// <summary>
    /// The aggregate audit is this version's own and has no counterpart in the other one, so it goes into the same
    /// key under a name of this version and is not mistaken for the audit of a license.
    /// </summary>
    [Fact]
    public void TheAggregateAuditIsNotAnAuditOfALicense()
    {
        var auditTime = new DateTime( 2026, 9, 18, 9, 0, 0, DateTimeKind.Utc );

        this.Update( c => c with { LastMatomoAuditTime = auditTime } );

        var configuration = this.Read();

        Assert.Equal( auditTime, configuration.LastMatomoAuditTime!.Value.ToUniversalTime() );
        Assert.Empty( configuration.LastAuditTimesByLong );
        Assert.Null( configuration.LastAuditTimesByString );
    }
}
