// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using JetBrains.Annotations;
using SharpCrafters.Backstage.Diagnostics;
using SharpCrafters.Backstage.Extensibility;
using SharpCrafters.Backstage.Infrastructure;
using SharpCrafters.Backstage.Threading;
using System;
using System.Collections.Generic;

namespace SharpCrafters.Backstage.Configuration.Registry;

/// <summary>
/// An <see cref="IConfigurationManager"/> that keeps the configuration objects a product shares with its earlier
/// versions in the Windows registry, and leaves every other object to a file-based manager.
/// </summary>
/// <remarks>
/// <para>
/// PostSharp 2026.0 keeps its settings in the registry and will run beside PostSharp 2027 on the same machine. A
/// license registered in either version has to be visible to the other, which means reading and writing the same
/// values rather than importing them once. The settings that 2026.0 does not have stay in files, because there is
/// nothing to share and a file is the better store.
/// </para>
/// <para>
/// There is no registry off Windows, so every type falls through to the file-based manager there. PostSharp 2026.0
/// has no configuration at all on those platforms, so nothing is shared and nothing is lost.
/// </para>
/// </remarks>
[PublicAPI]
public sealed class RegistryConfigurationManager : IConfigurationManager
{
    /// <summary>
    /// How long a write waits for the lock protecting its key. It matches the file-based manager.
    /// </summary>
    private static readonly TimeSpan _lockTimeout = TimeSpan.FromSeconds( 5 );

    private readonly IConfigurationManager _fileConfigurationManager;
    private readonly IRegistryService _registryService;
    private readonly INamedLockService _lockService;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly Dictionary<Type, IRegistryConfigurationSchema> _schemas = [];
    private readonly Dictionary<string, INamedLock> _locks = new( StringComparer.OrdinalIgnoreCase );
    private readonly object _locksSync = new();

    private int _isDisposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="RegistryConfigurationManager"/> class.
    /// </summary>
    /// <param name="serviceProvider">The service provider.</param>
    /// <param name="fileConfigurationManager">The manager that holds the objects this one does not map.</param>
    /// <param name="schemas">The objects that live in the registry, and where.</param>
    public RegistryConfigurationManager(
        IServiceProvider serviceProvider,
        IConfigurationManager fileConfigurationManager,
        IEnumerable<IRegistryConfigurationSchema> schemas )
    {
        this._fileConfigurationManager = fileConfigurationManager;
        this._registryService = serviceProvider.GetRequiredBackstageService<IRegistryService>();
        this._lockService = serviceProvider.GetRequiredBackstageService<INamedLockService>();
        this._dateTimeProvider = serviceProvider.GetRequiredBackstageService<IDateTimeProvider>();
        this.Logger = serviceProvider.GetRequiredBackstageService<EarlyLoggerFactory>().GetLogger( "Configuration" );

        // The registry is not available on every platform, and a schema that cannot be reached is worse than no
        // schema: it would answer every read with a default and swallow every write.
        if ( this._registryService.IsSupported )
        {
            foreach ( var schema in schemas )
            {
                this._schemas.Add( schema.ConfigurationType, schema );
            }
        }

        // Announcing a change of a file-based object as our own, so that a subscriber sees one event stream
        // whichever store the object came from.
        this._fileConfigurationManager.ConfigurationFileChanged += this.OnFileConfigurationChanged;
    }

    /// <inheritdoc />
    public ILogger Logger { get; }

    /// <summary>
    /// Gets the schema of a type, or <see langword="null"/> when the type is not kept in the registry.
    /// </summary>
    private IRegistryConfigurationSchema? GetSchema( Type type ) => this._schemas.TryGetValue( type, out var schema ) ? schema : null;

    /// <inheritdoc />
    public ConfigurationStore GetStore( Type type )
    {
        var schema = this.GetSchema( type );

        return schema == null
            ? this._fileConfigurationManager.GetStore( type )
            : new ConfigurationStore( ConfigurationStoreKind.RegistryKey, this._registryService.GetDisplayPath( schema.Hive, schema.KeyPath ) );
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// The registry is read on every call, and <paramref name="ignoreCache"/> therefore changes nothing: there is
    /// no cache to skip. A configuration object maps onto many values, and the registry offers no way to read them
    /// as one, so a cached object could not be told to be stale without re-reading the values that would have
    /// answered the question. Reading through is both simpler and more correct, and the cost is a handful of
    /// registry reads, which are cheap.
    /// </para>
    /// <para>
    /// The absence of an atomic read has a consequence that no implementation can remove: a read that runs while
    /// another process writes may see some values from before the write and some from after. The values of one
    /// object are written together and rarely, so the window is small, but a caller that must not see a torn
    /// object has to take the lock itself.
    /// </para>
    /// </remarks>
    public ConfigurationFile Get( Type type, bool ignoreCache = false )
    {
        var schema = this.GetSchema( type );

        if ( schema == null )
        {
            return this._fileConfigurationManager.Get( type, ignoreCache );
        }

        using var key = this._registryService.OpenKey( schema.Hive, schema.KeyPath );

        var value = schema.Read( key );

        if ( key != null )
        {
            // The timestamp says that the object was read from a store rather than being a fresh default, which is
            // what tells an update that there is something to compare against.
            value.SetFileSystemTimestamp( this._dateTimeProvider.UtcNow );
        }

        return value;
    }

    /// <inheritdoc />
    public event Action<ConfigurationFile>? ConfigurationFileChanged;

    private void OnFileConfigurationChanged( ConfigurationFile value ) => this.ConfigurationFileChanged?.Invoke( value );

    /// <inheritdoc />
    public ConfigurationUpdateOutcome Update( Type type, Func<ConfigurationFile, ConfigurationFile?> transform )
    {
        var schema = this.GetSchema( type );

        if ( schema == null )
        {
            return this._fileConfigurationManager.Update( type, transform );
        }

        ConfigurationUpdateScope.VerifyNotNested( type.Name );

        var lockName = this._registryService.GetDisplayPath( schema.Hive, schema.KeyPath ) + "!" + type.Name;

        if ( !this.TryAcquireLock( lockName, out var releaser ) )
        {
            return ConfigurationUpdateOutcome.LockTimeout;
        }

        ConfigurationFile? valueToAnnounce;
        ConfigurationUpdateOutcome outcome;

        using ( releaser )
        {
            outcome = this.UpdateWithinLock( schema, type, transform, out valueToAnnounce );
        }

        if ( valueToAnnounce != null )
        {
            ConfigurationUpdateScope.RaiseConfigurationFileChanged( this.ConfigurationFileChanged, valueToAnnounce, this.Logger );
        }

        return outcome;
    }

    private ConfigurationUpdateOutcome UpdateWithinLock(
        IRegistryConfigurationSchema schema,
        Type type,
        Func<ConfigurationFile, ConfigurationFile?> transform,
        out ConfigurationFile? valueToAnnounce )
    {
        valueToAnnounce = null;

        // Created rather than opened, so that the first write of a product that has never run has somewhere to go.
        using var key = this._registryService.CreateKey( schema.Hive, schema.KeyPath );

        if ( key == null )
        {
            this.Logger.Warning?.Log( $"Cannot open '{this._registryService.GetDisplayPath( schema.Hive, schema.KeyPath )}' for writing." );

            return ConfigurationUpdateOutcome.WriteFailed;
        }

        var currentValue = schema.Read( key );
        currentValue.SetFileSystemTimestamp( this._dateTimeProvider.UtcNow );

        ConfigurationFile? newValue;

        using ( ConfigurationUpdateScope.Enter( type.Name ) )
        {
            newValue = transform( currentValue );
        }

        if ( newValue == null )
        {
            return ConfigurationUpdateOutcome.Declined;
        }

        if ( newValue.Equals( currentValue ) )
        {
            return ConfigurationUpdateOutcome.NoChange;
        }

        // Incremented from the current value and not from the one the transformation produced, for the reason the
        // file-based manager gives: the version counts the writes made to the object, so a transformation that
        // builds a fresh instance must not take it back to one.
        var valueToStore = newValue with { Version = (currentValue.Version ?? 0) + 1 };

        try
        {
            schema.Write( key, valueToStore );
        }
        catch ( Exception e )
        {
            this.Logger.LogException( e, $"Cannot write '{this._registryService.GetDisplayPath( schema.Hive, schema.KeyPath )}'" );

            return ConfigurationUpdateOutcome.WriteFailed;
        }

        valueToStore.SetFileSystemTimestamp( this._dateTimeProvider.UtcNow );
        valueToAnnounce = valueToStore;

        return ConfigurationUpdateOutcome.Updated;
    }

    /// <summary>
    /// Acquires the lock protecting one key, and never throws: failing to write a configuration object must not
    /// fail the operation that happened to trigger it.
    /// </summary>
    /// <remarks>
    /// The lock excludes the other processes of this version only. PostSharp 2026.0 takes no such lock, so a write
    /// that races with one of its own is settled by the registry, value by value, and the last writer wins. Making
    /// that impossible would mean changing the earlier version, which is what this whole design exists to avoid.
    /// </remarks>
    private bool TryAcquireLock( string lockName, out IDisposable? releaser )
    {
        try
        {
            if ( this.GetLock( lockName ).TryAcquire( _lockTimeout, out releaser ) )
            {
                return true;
            }

            this.Logger.Warning?.Log( $"Timeout while waiting {_lockTimeout.TotalSeconds} s for the lock protecting '{lockName}'." );

            return false;
        }
        catch ( Exception e )
        {
            this.Logger.LogException( e, $"Cannot acquire the lock protecting '{lockName}'" );

            releaser = null;

            return false;
        }
    }

    private INamedLock GetLock( string lockName )
    {
        lock ( this._locksSync )
        {
            if ( this._isDisposed != 0 )
            {
                throw new ObjectDisposedException( nameof(RegistryConfigurationManager) );
            }

            if ( !this._locks.TryGetValue( lockName, out var namedLock ) )
            {
                namedLock = this._lockService.GetGlobalLock( lockName );
                this._locks.Add( lockName, namedLock );
            }

            return namedLock;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        List<INamedLock> locksToDispose;

        lock ( this._locksSync )
        {
            if ( this._isDisposed != 0 )
            {
                return;
            }

            this._isDisposed = 1;
            locksToDispose = [.. this._locks.Values];
            this._locks.Clear();
        }

        this._fileConfigurationManager.ConfigurationFileChanged -= this.OnFileConfigurationChanged;

        foreach ( var namedLock in locksToDispose )
        {
            namedLock.Dispose();
        }

        this._fileConfigurationManager.Dispose();
    }
}
