// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

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
/// Tests that a long-running process notices a setting that another process changed in the registry.
/// </summary>
/// <remarks>
/// The other process is usually another version of the product. The direction from this version to that one already
/// works, because a license registered here writes the timestamp that version watches; this is the other direction,
/// which needs a watch of our own.
/// </remarks>
public sealed class RegistryConfigurationWatcherTests : TestsBase
{
    private const string _keyPath = @"Software\SharpCrafters\Test";

    private readonly TestRegistryService _registry = new();

    public RegistryConfigurationWatcherTests( ITestOutputHelper logger )
        : base( logger, new TestApplicationInfo { IsLongRunningProcess = true } ) { }

    protected override void ConfigureServices( ServiceProviderBuilder services )
    {
        base.ConfigureServices( services );
        services.AddService( typeof(IRegistryService), this._registry );
    }

    private RegistryConfigurationManager CreateManager()
        => new( this.ServiceProvider, new InMemoryConfigurationManager( this.ServiceProvider ), [new TestRegistryConfigurationSchema()] );

    /// <summary>
    /// Writes into the hive the way another process would, and announces it the way the registry would: the
    /// notification says that something under the key changed, not what.
    /// </summary>
    private void ChangeBehindTheManager( Action<IRegistryKey> change )
    {
        change( this._registry.GetOrCreateKey( RegistryHiveKind.CurrentUser, _keyPath ) );
        this._registry.NotifyChange( RegistryHiveKind.CurrentUser, _keyPath );
    }

    [Fact]
    public void ALongRunningProcessWatchesTheKey()
    {
        using var manager = this.CreateManager();

        Assert.Equal( 1, this._registry.WatcherCount );
    }

    /// <summary>
    /// A change made by another process is announced, and the announced value is the new one.
    /// </summary>
    [Fact]
    public void AChangeOfAnotherProcessIsAnnounced()
    {
        using var manager = this.CreateManager();

        var announced = new List<ConfigurationFile>();
        manager.ConfigurationFileChanged += announced.Add;

        this.ChangeBehindTheManager( key => key.SetStringValue( "Text", "written by another process" ) );

        Assert.Single( announced );
        Assert.Equal( "written by another process", Assert.IsType<TestRegistryConfiguration>( announced[0] ).Text );
    }

    /// <summary>
    /// The key holds the settings of another version of the product beside ours, and the notification does not say
    /// which of them changed. A change to something we do not read is not a change of ours.
    /// </summary>
    [Fact]
    public void AChangeToASettingWeDoNotReadIsNotAnnounced()
    {
        using var manager = this.CreateManager();

        var announced = new List<ConfigurationFile>();
        manager.ConfigurationFileChanged += announced.Add;

        this.ChangeBehindTheManager( key => key.SetStringValue( "PartnerId", "a setting only the other version has" ) );

        Assert.Empty( announced );
    }

    /// <summary>
    /// A notification that follows no change at all announces nothing. The registry coalesces changes and may
    /// notify more than once for one of them.
    /// </summary>
    [Fact]
    public void ANotificationWithoutAChangeAnnouncesNothing()
    {
        using var manager = this.CreateManager();
        manager.Update<TestRegistryConfiguration>( c => c with { Text = "hello" } );

        var announced = new List<ConfigurationFile>();
        manager.ConfigurationFileChanged += announced.Add;

        this._registry.NotifyChange( RegistryHiveKind.CurrentUser, _keyPath );
        this._registry.NotifyChange( RegistryHiveKind.CurrentUser, _keyPath );

        Assert.Empty( announced );
    }

    /// <summary>
    /// Our own write announces once. The write changes the key, so the notification it triggers arrives as well, and
    /// the two must not become two events.
    /// </summary>
    [Fact]
    public void OurOwnWriteIsAnnouncedOnce()
    {
        using var manager = this.CreateManager();

        var announced = new List<ConfigurationFile>();
        manager.ConfigurationFileChanged += announced.Add;

        manager.Update<TestRegistryConfiguration>( c => c with { Text = "hello" } );
        this._registry.NotifyChange( RegistryHiveKind.CurrentUser, _keyPath );

        Assert.Single( announced );
    }

    /// <summary>
    /// A change below the key is a change of the key, because a configuration object of this product spreads over
    /// both: the registered license keys live in a sub-key of the one that holds the rest of the settings.
    /// </summary>
    [Fact]
    public void AChangeBelowTheKeyIsAnnounced()
    {
        using var manager = this.CreateManager();

        var announced = new List<ConfigurationFile>();
        manager.ConfigurationFileChanged += announced.Add;

        this._registry.GetOrCreateKey( RegistryHiveKind.CurrentUser, _keyPath ).SetStringValue( "Text", "written by another process" );
        this._registry.NotifyChange( RegistryHiveKind.CurrentUser, _keyPath + @"\ASubKey" );

        Assert.Single( announced );
    }

    /// <summary>
    /// The watch is given up with the manager, so that a handle is not left behind.
    /// </summary>
    [Fact]
    public void DisposingTheManagerStopsTheWatch()
    {
        var manager = this.CreateManager();
        Assert.Equal( 1, this._registry.WatcherCount );

        manager.Dispose();

        Assert.Equal( 0, this._registry.WatcherCount );
    }

    /// <summary>
    /// A key that cannot be watched is not an error: the manager goes on reading through, and a change is seen at
    /// the next read rather than as it happens.
    /// </summary>
    [Fact]
    public void AKeyThatCannotBeWatchedIsNotAnError()
    {
        this._registry.CanWatchChanges = false;

        using var manager = this.CreateManager();

        Assert.Equal( 0, this._registry.WatcherCount );

        this._registry.GetOrCreateKey( RegistryHiveKind.CurrentUser, _keyPath ).SetStringValue( "Text", "written by another process" );

        Assert.Equal( "written by another process", manager.Get<TestRegistryConfiguration>().Text );
    }
}

/// <summary>
/// Tests that a process which ends in a moment does not ask to be told about changes it will never see.
/// </summary>
public sealed class RegistryConfigurationWatcherOfShortProcessTests : TestsBase
{
    private readonly TestRegistryService _registry = new();

    public RegistryConfigurationWatcherOfShortProcessTests( ITestOutputHelper logger )
        : base( logger, new TestApplicationInfo { IsLongRunningProcess = false } ) { }

    protected override void ConfigureServices( ServiceProviderBuilder services )
    {
        base.ConfigureServices( services );
        services.AddService( typeof(IRegistryService), this._registry );
    }

    [Fact]
    public void AShortProcessDoesNotWatch()
    {
        using var manager = new RegistryConfigurationManager(
            this.ServiceProvider,
            new InMemoryConfigurationManager( this.ServiceProvider ),
            [new TestRegistryConfigurationSchema()] );

        Assert.Equal( 0, this._registry.WatcherCount );
    }
}
