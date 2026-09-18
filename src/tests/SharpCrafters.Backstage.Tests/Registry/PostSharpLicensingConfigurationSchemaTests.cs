// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using PostSharp.Backstage.Configuration;
using SharpCrafters.Backstage.Configuration;
using SharpCrafters.Backstage.Configuration.Registry;
using SharpCrafters.Backstage.Extensibility;
using SharpCrafters.Backstage.Licensing;
using SharpCrafters.Backstage.Testing;
using System;
using System.Collections.Immutable;
using System.Linq;
using Xunit;
using Xunit.Abstractions;

namespace SharpCrafters.Backstage.Tests.Registry;

/// <summary>
/// Tests that the registered license keys of PostSharp live where PostSharp 2026.0 puts them.
/// </summary>
/// <remarks>
/// These are compatibility tests, not round-trip tests. Each one either seeds the hive with the exact value names
/// and kinds that PostSharp 2026.0 writes and asserts that this version reads them, or writes through this version
/// and asserts the names and kinds that the other version will look for. A round trip through this version alone
/// would pass however both halves drifted.
/// </remarks>
public sealed class PostSharpLicensingConfigurationSchemaTests : TestsBase
{
    private const string _rootKeyPath = @"Software\SharpCrafters\PostSharp 3";
    private const string _licenseKeysKeyPath = _rootKeyPath + @"\LicenseKeys";

    private readonly TestRegistryService _registry = new();

    public PostSharpLicensingConfigurationSchemaTests( ITestOutputHelper logger ) : base( logger ) { }

    protected override void ConfigureServices( ServiceProviderBuilder services )
    {
        base.ConfigureServices( services );
        services.AddService( typeof(IRegistryService), this._registry );
    }

    private RegistryConfigurationManager CreateManager()
        => new(
            this.ServiceProvider,
            new InMemoryConfigurationManager( this.ServiceProvider ),
            [new PostSharpLicensingConfigurationSchema( this.Time )] );

    private IRegistryKey RootKey() => this._registry.GetOrCreateKey( RegistryHiveKind.CurrentUser, _rootKeyPath );

    private IRegistryKey LicenseKeysKey() => this._registry.GetOrCreateKey( RegistryHiveKind.CurrentUser, _licenseKeysKeyPath );

    private LicensingConfiguration Read()
    {
        using var manager = this.CreateManager();

        return manager.Get<LicensingConfiguration>();
    }

    private void Update( Func<LicensingConfiguration, LicensingConfiguration> transform )
    {
        using var manager = this.CreateManager();
        manager.Update( typeof(LicensingConfiguration), current => transform( (LicensingConfiguration) current ) );
    }

    [Fact]
    public void TheLicensesLiveUnderTheKeyOfPostSharp2026()
    {
        using var manager = this.CreateManager();

        var store = manager.GetStore( typeof(LicensingConfiguration) );

        Assert.Equal( ConfigurationStoreKind.RegistryKey, store.Kind );
        Assert.Equal( @"HKEY_CURRENT_USER\Software\SharpCrafters\PostSharp 3", store.Path );
    }

    /// <summary>
    /// The value that PostSharp 3.0 used is still read, which is what makes a machine set up long ago keep working.
    /// PostSharp 2026.0 never writes it and only deletes it, and neither does this version.
    /// </summary>
    [Fact]
    public void TheLicenseOfPostSharp30IsStillRead()
    {
        this.RootKey().SetStringValue( "LicenseKey", "the-legacy-key" );

        Assert.Equal( "the-legacy-key", this.Read().LegacyLicense );
    }

    /// <summary>
    /// The registered keys are the values of the LicenseKeys sub-key, whose names are decimal indices.
    /// </summary>
    [Fact]
    public void TheRegisteredKeysAreReadFromTheLicenseKeysSubKey()
    {
        var key = this.LicenseKeysKey();
        key.SetStringValue( "0", "first-key" );
        key.SetStringValue( "1", "second-key" );

        Assert.Equal( ["first-key", "second-key"], this.Read().Licenses.ToArray() );
    }

    /// <summary>
    /// The values are read in the order of their indices, whatever order the registry enumerates them in, so that
    /// the license consumed first is the one the user registered first.
    /// </summary>
    [Fact]
    public void TheRegisteredKeysAreOrderedByTheirIndex()
    {
        var key = this.LicenseKeysKey();
        key.SetStringValue( "10", "eleventh-key" );
        key.SetStringValue( "2", "third-key" );
        key.SetStringValue( "0", "first-key" );

        Assert.Equal( ["first-key", "third-key", "eleventh-key"], this.Read().Licenses.ToArray() );
    }

    /// <summary>
    /// A value whose name is not an index is read too. PostSharp has always read them all, and a user may have
    /// added one by hand.
    /// </summary>
    [Fact]
    public void AValueThatIsNotNamedAfterAnIndexIsReadToo()
    {
        var key = this.LicenseKeysKey();
        key.SetStringValue( "0", "first-key" );
        key.SetStringValue( "AddedByHand", "hand-written-key" );

        Assert.Equal( ["first-key", "hand-written-key"], this.Read().Licenses.ToArray() );
    }

    /// <summary>
    /// A blank value is what an expired lease leaves behind, and it is not a license key.
    /// </summary>
    [Fact]
    public void ABlankValueIsNotALicense()
    {
        var key = this.LicenseKeysKey();
        key.SetStringValue( "0", "" );
        key.SetStringValue( "1", "a-key" );

        Assert.Equal( ["a-key"], this.Read().Licenses.ToArray() );
    }

    /// <summary>
    /// A key that requires a later version lives in a sub-key named after that version, with three components,
    /// which is the only spelling PostSharp 2026.0 reads.
    /// </summary>
    [Fact]
    public void AVersionedKeyIsReadFromItsVersionSubKey()
    {
        this._registry.GetOrCreateKey( RegistryHiveKind.CurrentUser, _licenseKeysKeyPath + @"\2027.0.0" )
            .SetStringValue( "0", "a-2027-key" );

        var groups = this.Read().LicensesByMinimalVersion;

        Assert.NotNull( groups );
        Assert.Equal( ["a-2027-key"], groups["2027.0.0"].ToArray() );
    }

    /// <summary>
    /// A sub-key whose name is not a version belongs to something else and is left alone. PostSharp 2026.0 keeps
    /// its lease cache and its audit records beside the license keys.
    /// </summary>
    [Fact]
    public void ASubKeyThatIsNotAVersionIsNotAGroup()
    {
        this._registry.GetOrCreateKey( RegistryHiveKind.CurrentUser, _licenseKeysKeyPath + @"\NotAVersion" )
            .SetStringValue( "0", "not-a-license-of-ours" );

        Assert.Null( this.Read().LicensesByMinimalVersion );
    }

    [Fact]
    public void TheTrialStartDateIsTheEvaluationValue()
    {
        var startDate = new DateTime( 2026, 9, 18, 0, 0, 0, DateTimeKind.Utc );
        this.RootKey().SetQWordValue( "Evaluation", RegistryValueCodec.DateTimeToQWord( startDate ) );

        Assert.Equal( startDate, this.Read().LastEvaluationStartDate!.Value.ToUniversalTime() );
    }

    /// <summary>
    /// Registering a key writes it where PostSharp 2026.0 looks: a value of the LicenseKeys sub-key, named after
    /// the first free index and of the string kind.
    /// </summary>
    [Fact]
    public void RegisteringAKeyWritesItWherePostSharp2026Looks()
    {
        this.Update( c => c with { Licenses = ImmutableArray.Create<string?>( "a-key" ) } );

        var key = this.LicenseKeysKey();
        Assert.Equal( "a-key", key.GetValue( "0" ) );
        Assert.IsType<string>( key.GetValue( "0" ) );
    }

    /// <summary>
    /// A second key takes the next free index and leaves the first where it is. PostSharp 2026.0 finds a key by its
    /// content rather than by its name, but moving a value rewrites it for nothing.
    /// </summary>
    [Fact]
    public void ASecondKeyDoesNotMoveTheFirst()
    {
        this.Update( c => c with { Licenses = ImmutableArray.Create<string?>( "first-key" ) } );
        this.Update( c => c with { Licenses = ImmutableArray.Create<string?>( "first-key", "second-key" ) } );

        var key = this.LicenseKeysKey();
        Assert.Equal( "first-key", key.GetValue( "0" ) );
        Assert.Equal( "second-key", key.GetValue( "1" ) );
    }

    /// <summary>
    /// Unregistering a key removes its value, which is how the other version stops seeing it.
    /// </summary>
    [Fact]
    public void UnregisteringAKeyRemovesItsValue()
    {
        this.Update( c => c with { Licenses = ImmutableArray.Create<string?>( "first-key", "second-key" ) } );
        this.Update( c => c with { Licenses = ImmutableArray.Create<string?>( "second-key" ) } );

        var key = this.LicenseKeysKey();
        Assert.Equal( ["second-key"], key.GetValueNames().Select( key.GetString ).ToArray() );
    }

    /// <summary>
    /// A group is written to a sub-key named with three components, whatever spelling the object used, because that
    /// is the only one PostSharp 2026.0 reads.
    /// </summary>
    [Fact]
    public void AGroupIsWrittenWithThreeComponents()
    {
        this.Update(
            c => c with
            {
                LicensesByMinimalVersion = ImmutableDictionary<string, ImmutableArray<string?>>.Empty
                    .Add( "2027.0", ImmutableArray.Create<string?>( "a-2027-key" ) )
            } );

        Assert.True( this._registry.KeyExists( RegistryHiveKind.CurrentUser, _licenseKeysKeyPath + @"\2027.0.0" ) );

        Assert.Equal(
            "a-2027-key",
            this._registry.GetOrCreateKey( RegistryHiveKind.CurrentUser, _licenseKeysKeyPath + @"\2027.0.0" ).GetValue( "0" ) );
    }

    /// <summary>
    /// Registering a license writes the timestamp that PostSharp 2026.0 watches. Without it, a license registered
    /// here stays invisible to an instance of that version which is already running.
    /// </summary>
    [Fact]
    public void RegisteringALicenseTellsTheOtherVersion()
    {
        Assert.Null( this.RootKey().GetValue( "LicenseTimestamp" ) );

        this.Update( c => c with { Licenses = ImmutableArray.Create<string?>( "a-key" ) } );

        var timestamp = this.RootKey().GetValue( "LicenseTimestamp" );
        Assert.IsType<long>( timestamp );
        Assert.NotNull( RegistryValueCodec.QWordToDateTime( timestamp ) );
    }

    /// <summary>
    /// The timestamp only ever moves forward, because the watcher of the other version compares it with the one it
    /// last saw. Two changes within the resolution of the clock would otherwise leave the second unnoticed.
    /// </summary>
    [Fact]
    public void TheTimestampAlwaysMovesForward()
    {
        this.Update( c => c with { Licenses = ImmutableArray.Create<string?>( "first-key" ) } );
        var firstTimestamp = (long) this.RootKey().GetValue( "LicenseTimestamp" )!;

        // The clock of the test does not advance on its own, so this stands for two changes in the same
        // millisecond.
        this.Update( c => c with { Licenses = ImmutableArray.Create<string?>( "first-key", "second-key" ) } );
        var secondTimestamp = (long) this.RootKey().GetValue( "LicenseTimestamp" )!;

        Assert.True( secondTimestamp > firstTimestamp, $"The timestamp went from {firstTimestamp} to {secondTimestamp}." );
    }

    /// <summary>
    /// An update that changes no license does not touch the timestamp, so that the other version is not woken for
    /// nothing.
    /// </summary>
    [Fact]
    public void ASettingThatIsNotALicenseDoesNotTellTheOtherVersion()
    {
        this.Update( c => c with { Licenses = ImmutableArray.Create<string?>( "a-key" ) } );
        var timestampAfterRegistration = (long) this.RootKey().GetValue( "LicenseTimestamp" )!;

        this.Update( c => c with { Version = c.Version } );

        Assert.Equal( timestampAfterRegistration, (long) this.RootKey().GetValue( "LicenseTimestamp" )! );
    }

    /// <summary>
    /// The settings that PostSharp 2026.0 keeps in the same key are not ours to delete.
    /// </summary>
    [Fact]
    public void TheSettingsOfTheOtherVersionSurviveAnUpdate()
    {
        var key = this.RootKey();
        key.SetDWordValue( "LicenseMessagesDisabled", 1 );
        key.SetStringValue( "LatestStartedVsxVersion", "2026.0.1.123" );
        key.SetQWordValue( "VsxDetectedTime", 1234567 );

        this.Update( c => c with { Licenses = ImmutableArray.Create<string?>( "a-key" ) } );

        Assert.Equal( 1, key.GetValue( "LicenseMessagesDisabled" ) );
        Assert.Equal( "2026.0.1.123", key.GetValue( "LatestStartedVsxVersion" ) );
        Assert.Equal( 1234567L, key.GetValue( "VsxDetectedTime" ) );
    }

    /// <summary>
    /// A machine on which PostSharp 2026.0 has already run is read whole: the legacy value, the indexed keys and
    /// the versioned group together.
    /// </summary>
    [Fact]
    public void AMachineSetUpByTheOtherVersionIsReadWhole()
    {
        // Real license keys, because the consumer parses what it reads and silently drops what does not parse: a
        // test made of invented strings would prove that the buckets are read and not that they are usable.
        var licenseKeyProvider = new TestLicenseKeyProvider();
        var legacyKey = licenseKeyProvider.PostSharpUltimate;
        var currentKey = licenseKeyProvider.PostSharpFramework;
        var versionedKey = licenseKeyProvider.PostSharpCaching;
        const string licenseServerUrl = "https://licenses.example.com/postsharp";

        var root = this.RootKey();
        root.SetStringValue( "LicenseKey", legacyKey );
        root.SetQWordValue( "Evaluation", RegistryValueCodec.DateTimeToQWord( new DateTime( 2026, 8, 1, 0, 0, 0, DateTimeKind.Utc ) ) );

        var licenseKeys = this.LicenseKeysKey();
        licenseKeys.SetStringValue( "0", currentKey );
        licenseKeys.SetStringValue( "1", licenseServerUrl );

        this._registry.GetOrCreateKey( RegistryHiveKind.CurrentUser, _licenseKeysKeyPath + @"\6.9.3" )
            .SetStringValue( "0", versionedKey );

        var configuration = this.Read();

        Assert.Equal( legacyKey, configuration.LegacyLicense );
        Assert.Equal( [currentKey, licenseServerUrl], configuration.Licenses.ToArray() );
        Assert.Equal( [versionedKey], configuration.LicensesByMinimalVersion!["6.9.3"].ToArray() );
        Assert.Equal( new DateTime( 2026, 8, 1, 0, 0, 0, DateTimeKind.Utc ), configuration.LastEvaluationStartDate!.Value.ToUniversalTime() );

        // Every bucket reaches the consumer, in the order in which the buckets were introduced, and the license
        // server comes last so that a registered key is always considered before a lease is acquired.
        Assert.Equal(
            [legacyKey, currentKey, versionedKey, licenseServerUrl],
            configuration.GetRegisteredLicenseStrings( new Version( 2027, 0 ) ).ToArray() );
    }
}
