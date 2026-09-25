// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using JetBrains.Annotations;
using SharpCrafters.Backstage.Application;
using SharpCrafters.Backstage.Diagnostics;
using SharpCrafters.Backstage.Extensibility;
using SharpCrafters.Backstage.Serialization;
using SharpCrafters.Backstage.Infrastructure;
using SharpCrafters.Backstage.Threading;
using System;
using System.Collections.Generic;
using System.Linq;

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
    private readonly IJsonSerializationService _jsonSerializationService;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly Dictionary<Type, IRegistryConfigurationSchema> _schemas = [];
    private readonly Dictionary<string, INamedLock> _locks = new( StringComparer.OrdinalIgnoreCase );
    private readonly object _locksSync = new();
    private readonly List<IDisposable> _watchers = [];

    /// <summary>
    /// The value last read or written for each type, which is what tells a notification that something this manager
    /// cares about has changed.
    /// </summary>
    /// <remarks>
    /// This is not a read cache. <see cref="Get"/> always reads the registry; it only records what it read, so that
    /// a notification has something to compare against. The registry says that something under a key changed and not
    /// what, and a key holds the values of another version of the product as well as ours, so without this every
    /// change to anything would be announced as a change to everything.
    /// </remarks>
    private readonly Dictionary<Type, ConfigurationFile> _lastKnownValues = [];

    private readonly object _lastKnownValuesSync = new();

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
        this._jsonSerializationService = serviceProvider.GetRequiredBackstageService<IJsonSerializationService>();
        this._dateTimeProvider = serviceProvider.GetRequiredBackstageService<IDateTimeProvider>();
        this.Logger = serviceProvider.GetRequiredBackstageService<EarlyLoggerFactory>().GetLogger( "Configuration" );

        var applicationInfo = serviceProvider.GetBackstageService<IApplicationInfoProvider>()?.Application;

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

        // A process that ends in a moment learns nothing from a notification, and asking for one costs a handle and
        // a registration. This matches the file-based manager, which watches only for such a process.
        if ( applicationInfo is { IsLongRunningProcess: true } )
        {
            this.StartWatching();
        }
    }

    /// <summary>
    /// Asks to be told when a key that holds one of our objects changes.
    /// </summary>
    /// <remarks>
    /// One watch per key rather than one per object, because two objects may share a key, and because a watch covers
    /// the sub-keys as well: the key that holds the licensing settings is the parent of the one that holds the
    /// registered license keys.
    /// </remarks>
    private void StartWatching()
    {
        foreach ( var keyGroup in this._schemas.Values.GroupBy( schema => (schema.Hive, schema.KeyPath) ) )
        {
            var typesAtKey = keyGroup.Select( schema => schema.ConfigurationType ).ToList();

            // Read once, so that the first notification has something to compare against. Without it, the first
            // change to anything under the key -- including a setting of the other version of the product, which
            // this one never reads -- would be announced as a change to every object held there.
            foreach ( var type in typesAtKey )
            {
                this.RememberValue( type, this.Get( type ) );
            }

            var watcher = this._registryService.WatchChanges(
                keyGroup.Key.Hive,
                keyGroup.Key.KeyPath,
                () => this.OnRegistryChanged( typesAtKey ) );

            if ( watcher != null )
            {
                this._watchers.Add( watcher );
            }
            else
            {
                this.Logger.Trace?.Log( "Cannot watch " + this._registryService.GetDisplayPath( keyGroup.Key.Hive, keyGroup.Key.KeyPath ) + " for changes." );
            }
        }
    }

    /// <summary>
    /// Re-reads the objects held at a key that has changed, and announces the ones that differ from what was last
    /// read or written.
    /// </summary>
    /// <remarks>
    /// The notification says that something under the key changed and not what, and the key holds the settings of
    /// another version of the product beside ours, so most notifications concern nothing of ours. Comparing is what
    /// keeps a change of theirs from being announced as a change of ours.
    /// </remarks>
    private void OnRegistryChanged( IReadOnlyList<Type> types )
    {
        foreach ( var type in types )
        {
            ConfigurationFile currentValue;

            try
            {
                currentValue = this.Get( type );
            }
            catch ( Exception e )
            {
                this.Logger.LogException( e, "Cannot read " + type.Name + " after a change of the registry" );

                continue;
            }

            bool hasChanged;

            lock ( this._lastKnownValuesSync )
            {
                hasChanged = !this._lastKnownValues.TryGetValue( type, out var lastKnownValue ) || !this.StructurallyEquals( currentValue, lastKnownValue );
                this._lastKnownValues[type] = currentValue;
            }

            if ( hasChanged )
            {
                ConfigurationUpdateScope.RaiseConfigurationFileChanged( this.ConfigurationFileChanged, currentValue, this.Logger );
            }
        }
    }

    /// <summary>
    /// Determines whether two configuration objects hold the same content.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The moment at which an object was read, and the number of writes made to it, are the bookkeeping of the store
    /// and not content. They are part of the object and therefore of its equality, so two reads of a key that nobody
    /// has touched are never equal, and a notification would announce a change every time it arrived.
    /// </para>
    /// <para>
    /// The comparison is of the JSON of the two objects, as the file-based manager does it, and not of the objects
    /// themselves. The equality a record generates compares each member with its own equality, and the equality of an
    /// immutable array or dictionary is the identity of the array behind it: two reads of the same key produce two
    /// arrays holding the same strings and comparing as different. Every object held here has such a member, so the
    /// generated equality answers false for every pair that was not read once and compared with itself.
    /// </para>
    /// </remarks>
    private bool StructurallyEquals( ConfigurationFile a, ConfigurationFile b )
    {
        var type = a.GetType();

        if ( type != b.GetType() )
        {
            return false;
        }

        return string.Equals(
            this._jsonSerializationService.Serialize( a with { Version = null }, type ),
            this._jsonSerializationService.Serialize( b with { Version = null }, type ),
            StringComparison.Ordinal );
    }

    /// <summary>
    /// Records the value that a notification will compare against.
    /// </summary>
    private void RememberValue( Type type, ConfigurationFile value )
    {
        lock ( this._lastKnownValuesSync )
        {
            this._lastKnownValues[type] = value;
        }
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

        // The lock is the one the file-based manager takes, under a name derived from the key instead of the file, so
        // that two processes of this version do not interleave a read and a write of the same object.
        //
        // It is not shared with PostSharp 2026.0, and cannot be: that version writes these values with a plain
        // SetValue and takes no lock of any kind on this path, so there is no name to agree on. Nothing breaks — a
        // mutex only ever blocks a process that waits on it — but the guarantee is the weaker one, that two writers
        // of this version serialize and that a writer of the other version is last-writer-wins against them.
        //
        // The damage a lost race can do is bounded by what a write touches: a schema writes the values it maps and
        // never clears the key, so the two versions collide only on the same value of the same object, changed at the
        // same moment. A user registering a license in one version while the other writes the same one is the whole
        // of it, and the loser of that race is a registration the user can see did not take.
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
            // Recorded before the event, so that the notification which this write itself triggers finds the value
            // unchanged and does not announce it a second time.
            this.RememberValue( type, valueToAnnounce );

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

        if ( this.StructurallyEquals( newValue, currentValue ) )
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

        foreach ( var watcher in this._watchers )
        {
            watcher.Dispose();
        }

        this._watchers.Clear();

        foreach ( var namedLock in locksToDispose )
        {
            namedLock.Dispose();
        }

        this._fileConfigurationManager.Dispose();
    }
}