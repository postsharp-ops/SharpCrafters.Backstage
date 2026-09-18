// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using Metalama.Backstage;
using SharpCrafters.Backstage.Configuration;
using SharpCrafters.Backstage.Configuration.Registry;
using SharpCrafters.Backstage.Extensibility;
using SharpCrafters.Backstage.Testing;
using System;
using System.Collections.Generic;
using Xunit;
using Xunit.Abstractions;

namespace SharpCrafters.Backstage.Tests.Registry;

/// <summary>
/// Tests the manager that keeps a configuration object in the Windows registry.
/// </summary>
/// <remarks>
/// What is under test is not that a value survives a round trip, which the codec tests already pin, but the rules
/// that make two versions of a product able to share one key: only the values a schema names are ever touched, a
/// value that already holds the wanted content is not rewritten, and a change made by the other version is seen on
/// the next read.
/// </remarks>
public sealed class RegistryConfigurationManagerTests : TestsBase
{
    private const string _keyPath = @"Software\SharpCrafters\Test";

    private readonly TestRegistryService _registry = new();

    public RegistryConfigurationManagerTests( ITestOutputHelper logger )
        : base(
            logger,
            new BackstageInitializationOptions( new TestApplicationInfo(), MetalamaProduct.Instance )
            {
                AutoUploadTelemetry = false, AdditionalJsonTypeInfoResolvers = [TestRegistryJsonContext.Default]
            } ) { }

    protected override void ConfigureServices( ServiceProviderBuilder services )
    {
        base.ConfigureServices( services );
        services.AddService( typeof(IRegistryService), this._registry );
    }

    private RegistryConfigurationManager CreateManager( IEnumerable<IRegistryConfigurationSchema>? schemas = null )
        => new(
            this.ServiceProvider,
            new InMemoryConfigurationManager( this.ServiceProvider ),
            schemas ?? [new TestRegistryConfigurationSchema()] );

    /// <summary>
    /// Writes values into the hive the way the other version of the product would, so that a test can start from a
    /// machine on which that version has already run.
    /// </summary>
    private IRegistryKey SeedKey() => this._registry.GetOrCreateKey( RegistryHiveKind.CurrentUser, _keyPath );

    /// <summary>
    /// Updates a configuration object and yields the outcome, which the generic extension method hides behind a
    /// Boolean that cannot tell a declined transformation from one that changed nothing.
    /// </summary>
    private static ConfigurationUpdateOutcome Update<T>( IConfigurationManager manager, Func<T, ConfigurationFile?> transform )
        where T : ConfigurationFile
        => manager.Update( typeof(T), current => transform( (T) current ) );

    [Fact]
    public void AMappedTypeIsStoredInTheRegistryAndSaysSo()
    {
        using var manager = this.CreateManager();

        var store = manager.GetStore( typeof(TestRegistryConfiguration) );

        Assert.Equal( ConfigurationStoreKind.RegistryKey, store.Kind );
        Assert.Equal( @"HKEY_CURRENT_USER\Software\SharpCrafters\Test", store.Path );
    }

    /// <summary>
    /// A configuration object that the product does not share with its earlier versions is left to the file-based
    /// manager, which is what keeps the registry to the settings that have a counterpart there.
    /// </summary>
    [Fact]
    public void AnUnmappedTypeIsLeftToTheOtherManager()
    {
        using var manager = this.CreateManager();

        var value = manager.Get( typeof(TestConfigurationFileOfTheOtherManager) );

        Assert.IsType<TestConfigurationFileOfTheOtherManager>( value );
        Assert.True( manager.Update<TestConfigurationFileOfTheOtherManager>( c => c with { Value = "written" } ) );
        Assert.Equal( "written", manager.Get<TestConfigurationFileOfTheOtherManager>().Value );

        // Nothing of it reached the registry.
        Assert.False( this._registry.KeyExists( RegistryHiveKind.CurrentUser, _keyPath ) );
    }

    /// <summary>
    /// A key that does not exist is a product that has never run, not an error, and the defaults of the object are
    /// what it means. The default of a member is not always the default of its type.
    /// </summary>
    [Fact]
    public void AnAbsentKeyReadsAsTheDefaults()
    {
        using var manager = this.CreateManager();

        var value = manager.Get<TestRegistryConfiguration>();

        Assert.Null( value.Text );
        Assert.Null( value.Date );
        Assert.True( value.IsEnabled );
        Assert.Equal( 0, value.Count );
    }

    /// <summary>
    /// Values that the other version of the product wrote are read as they stand. This is the whole point of the
    /// exercise: the settings are shared rather than imported.
    /// </summary>
    [Fact]
    public void ValuesWrittenByTheOtherVersionAreRead()
    {
        var key = this.SeedKey();
        key.SetStringValue( "Text", "written by the other version" );
        key.SetQWordValue( "Date", RegistryValueConverters.DateTimeToQWord( new DateTime( 2026, 9, 18, 12, 0, 0, DateTimeKind.Utc ) ) );
        key.SetDWordValue( "IsEnabled", 0 );
        key.SetDWordValue( "Count", 42 );

        using var manager = this.CreateManager();
        var value = manager.Get<TestRegistryConfiguration>();

        Assert.Equal( "written by the other version", value.Text );
        Assert.Equal( new DateTime( 2026, 9, 18, 12, 0, 0, DateTimeKind.Utc ), value.Date!.Value.ToUniversalTime() );
        Assert.False( value.IsEnabled );
        Assert.Equal( 42, value.Count );
    }

    [Fact]
    public void AnUpdateWritesTheValues()
    {
        using var manager = this.CreateManager();

        var outcome = Update<TestRegistryConfiguration>( manager, c => c with { Text = "hello", Count = 7 } );

        Assert.Equal( ConfigurationUpdateOutcome.Updated, outcome );

        var key = this.SeedKey();
        Assert.Equal( "hello", key.GetValue( "Text" ) );
        Assert.Equal( 7, key.GetValue( "Count" ) );

        // And they are read back through a second manager, which has no cache to serve them from.
        using var otherManager = this.CreateManager();
        Assert.Equal( "hello", otherManager.Get<TestRegistryConfiguration>().Text );
    }

    /// <summary>
    /// The other version keeps its own values in the same key, and a value this version does not know is not a
    /// value it may delete.
    /// </summary>
    [Fact]
    public void AnUpdateLeavesTheValuesOfTheOtherVersionAlone()
    {
        var key = this.SeedKey();
        key.SetStringValue( "PartnerId", "a setting only the other version has" );
        key.SetQWordValue( "LastUploadTime", 1234567 );

        using var manager = this.CreateManager();
        manager.Update<TestRegistryConfiguration>( c => c with { Text = "hello" } );

        Assert.Equal( "a setting only the other version has", key.GetValue( "PartnerId" ) );
        Assert.Equal( 1234567L, key.GetValue( "LastUploadTime" ) );
    }

    /// <summary>
    /// A member that becomes absent deletes its value rather than writing an empty one, because absent is what the
    /// other version reads as "not set". This is how unregistering a license key removes it.
    /// </summary>
    [Fact]
    public void AMemberThatBecomesAbsentDeletesItsValue()
    {
        using var manager = this.CreateManager();
        manager.Update<TestRegistryConfiguration>( c => c with { Text = "hello", Date = new DateTime( 2026, 1, 1 ) } );

        var key = this.SeedKey();
        Assert.NotNull( key.GetValue( "Text" ) );

        manager.Update<TestRegistryConfiguration>( c => c with { Text = null, Date = null } );

        Assert.Null( key.GetValue( "Text" ) );
        Assert.Null( key.GetValue( "Date" ) );
        Assert.Null( manager.Get<TestRegistryConfiguration>().Text );
    }

    /// <summary>
    /// A value that already holds the wanted content is not rewritten. An update usually changes one member of a
    /// dozen, and each write reaches the disk and moves the last-write time of the key, which is what tells another
    /// process that something has changed.
    /// </summary>
    [Fact]
    public void OnlyTheValuesThatChangedAreWritten()
    {
        using var manager = this.CreateManager();
        manager.Update<TestRegistryConfiguration>( c => c with { Text = "hello", Count = 7 } );

        var writesAfterTheFirstUpdate = this._registry.WriteCount;

        manager.Update<TestRegistryConfiguration>( c => c with { Count = 8 } );

        // Count, and the version that every write bumps. Text, Date and IsEnabled are unchanged and untouched.
        Assert.Equal( 2, this._registry.WriteCount - writesAfterTheFirstUpdate );
    }

    [Fact]
    public void ATransformationThatDeclinesWritesNothing()
    {
        using var manager = this.CreateManager();

        Assert.Equal( ConfigurationUpdateOutcome.Declined, Update<TestRegistryConfiguration>( manager, _ => null ) );
        Assert.Equal( 0, this._registry.WriteCount );
    }

    [Fact]
    public void AnUpdateThatChangesNothingIsNotAWrite()
    {
        using var manager = this.CreateManager();
        manager.Update<TestRegistryConfiguration>( c => c with { Text = "hello" } );

        var writesAfterTheFirstUpdate = this._registry.WriteCount;

        Assert.Equal( ConfigurationUpdateOutcome.NoChange, Update<TestRegistryConfiguration>( manager, c => c ) );
        Assert.Equal( writesAfterTheFirstUpdate, this._registry.WriteCount );
    }

    /// <summary>
    /// The version counts the writes made to the object, so a transformation that builds a fresh instance must not
    /// take it back to one.
    /// </summary>
    [Fact]
    public void TheVersionCountsTheWrites()
    {
        using var manager = this.CreateManager();

        manager.Update<TestRegistryConfiguration>( c => c with { Count = 1 } );
        Assert.Equal( 1, manager.Get<TestRegistryConfiguration>().Version );

        manager.Update<TestRegistryConfiguration>( c => c with { Count = 2 } );
        Assert.Equal( 2, manager.Get<TestRegistryConfiguration>().Version );

        // A transformation that returns a fresh object rather than one derived from the current one.
        manager.Update<TestRegistryConfiguration>( _ => new TestRegistryConfiguration { Count = 3 } );
        Assert.Equal( 3, manager.Get<TestRegistryConfiguration>().Version );
    }

    [Fact]
    public void AChangeIsAnnouncedOnceItIsWritten()
    {
        using var manager = this.CreateManager();

        var announced = new List<ConfigurationFile>();
        manager.ConfigurationFileChanged += announced.Add;

        manager.Update<TestRegistryConfiguration>( c => c with { Text = "hello" } );
        Assert.Single( announced );
        Assert.Equal( "hello", Assert.IsType<TestRegistryConfiguration>( announced[0] ).Text );

        // Nothing changed, so there is nothing to announce.
        manager.Update<TestRegistryConfiguration>( c => c );
        Assert.Single( announced );
    }

    /// <summary>
    /// A transformation runs while the lock protecting its own key is held, so one that started a second update
    /// would make the thread hold two locks at once. It is refused rather than allowed to deadlock.
    /// </summary>
    [Fact]
    public void ATransformationCannotUpdateAnotherObject()
    {
        using var manager = this.CreateManager();

        Assert.Throws<InvalidOperationException>(
            () => manager.Update<TestRegistryConfiguration>(
                c =>
                {
                    manager.Update<TestRegistryConfiguration>( x => x with { Count = 1 } );

                    return c;
                } ) );
    }

    /// <summary>
    /// A change made behind the manager, which is what the other version of the product makes, is seen on the next
    /// read. The manager keeps no cache that could hide it.
    /// </summary>
    [Fact]
    public void AChangeMadeByAnotherWriterIsSeenImmediately()
    {
        using var manager = this.CreateManager();
        manager.Update<TestRegistryConfiguration>( c => c with { Text = "hello" } );

        this.SeedKey().SetStringValue( "Text", "changed by the other version" );

        Assert.Equal( "changed by the other version", manager.Get<TestRegistryConfiguration>().Text );
    }

    /// <summary>
    /// There is no registry off Windows, so every object falls through to the file-based manager. A schema that
    /// cannot be reached would be worse than none: it would answer every read with a default and swallow every
    /// write.
    /// </summary>
    [Fact]
    public void WithoutARegistryEverythingFallsThroughToTheOtherManager()
    {
        this._registry.IsSupported = false;

        using var manager = this.CreateManager();

        Assert.True( manager.Update<TestRegistryConfiguration>( c => c with { Text = "hello" } ) );
        Assert.Equal( "hello", manager.Get<TestRegistryConfiguration>().Text );
        Assert.Equal( 0, this._registry.WriteCount );
    }
}

/// <summary>
/// A configuration object that no schema maps, which the registry-backed manager therefore leaves to the file-based
/// one.
/// </summary>
[ConfigurationFile( "testOtherManager.json" )]
internal sealed record TestConfigurationFileOfTheOtherManager : ConfigurationFile
{
    public string? Value { get; init; }
}
