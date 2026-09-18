// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using JetBrains.Annotations;
using SharpCrafters.Backstage.Configuration.Registry;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SharpCrafters.Backstage.Testing;

/// <summary>
/// An <see cref="IRegistryService"/> that keeps a hive in memory, so that the components which store their
/// configuration in the registry can be tested without touching the registry of the machine that runs the test.
/// </summary>
/// <remarks>
/// It reproduces the behaviour the real implementation guarantees and that the callers depend upon: names of keys
/// and of values are compared without regard to case, a value keeps the kind it was written with, and reading a key
/// or a value that does not exist yields <see langword="null"/> rather than throwing.
/// </remarks>
[PublicAPI]
public sealed class TestRegistryService : IRegistryService
{
    private readonly Dictionary<RegistryHiveKind, TestRegistryKey> _hives;

    public TestRegistryService()
    {
        // Built in the constructor rather than in an initializer, so that the keys can count their writes on this
        // instance.
        this._hives = new Dictionary<RegistryHiveKind, TestRegistryKey>
        {
            [RegistryHiveKind.CurrentUser] = new( "HKEY_CURRENT_USER", this ),
            [RegistryHiveKind.LocalMachine] = new( "HKEY_LOCAL_MACHINE", this )
        };
    }

    /// <summary>
    /// Gets or sets a value indicating whether the registry exists. A test sets it to <see langword="false"/> to
    /// stand for a platform that has none.
    /// </summary>
    public bool IsSupported { get; set; } = true;

    /// <summary>
    /// Gets the number of writes made to any value, which a test uses to prove that writing a value that already
    /// holds the wanted content is skipped.
    /// </summary>
    public int WriteCount { get; private set; }

    /// <summary>
    /// Gets or sets a value indicating whether every write fails, which stands for a key the user may read but not
    /// write. The real implementation raises <see cref="UnauthorizedAccessException"/> there and lets it out, so this
    /// one does the same.
    /// </summary>
    public bool FailWrites { get; set; }

    private void CountWrite() => this.WriteCount++;

    public IRegistryKey? OpenKey( RegistryHiveKind hive, string keyPath, bool writable = false )
        => !this.IsSupported ? null : this._hives[hive].OpenPath( keyPath, false );

    public IRegistryKey? CreateKey( RegistryHiveKind hive, string keyPath )
        => !this.IsSupported ? null : this._hives[hive].OpenPath( keyPath, true );

    public string GetDisplayPath( RegistryHiveKind hive, string keyPath ) => this._hives[hive].DisplayPath + "\\" + keyPath;

    private readonly List<(RegistryHiveKind Hive, string KeyPath, Action OnChanged)> _watchers = [];

    /// <summary>
    /// Gets or sets a value indicating whether a key can be watched. A test sets it to <see langword="false"/> to
    /// stand for a platform, or a key, where the notification cannot be asked for.
    /// </summary>
    public bool CanWatchChanges { get; set; } = true;

    /// <summary>
    /// Gets the number of watches currently in place, which a test reads to prove that a watch was started and that
    /// it was stopped.
    /// </summary>
    public int WatcherCount => this._watchers.Count;

    public IDisposable? WatchChanges( RegistryHiveKind hive, string keyPath, Action onChanged )
    {
        if ( !this.IsSupported || !this.CanWatchChanges )
        {
            return null;
        }

        var watcher = (hive, keyPath, onChanged);
        this._watchers.Add( watcher );

        return new Watcher( this, watcher );
    }

    /// <summary>
    /// Announces a change to the watchers of a key, which stands for the change that another process would have
    /// made. The real notification says that something changed and not what, so this one does the same.
    /// </summary>
    public void NotifyChange( RegistryHiveKind hive, string keyPath )
    {
        foreach ( var watcher in this._watchers.ToList() )
        {
            // A watch covers the key and everything below it.
            if ( watcher.Hive == hive
                 && (keyPath.Equals( watcher.KeyPath, StringComparison.OrdinalIgnoreCase )
                     || keyPath.StartsWith( watcher.KeyPath + "\\", StringComparison.OrdinalIgnoreCase )) )
            {
                watcher.OnChanged();
            }
        }
    }

    private sealed class Watcher : IDisposable
    {
        private readonly TestRegistryService _service;
        private readonly (RegistryHiveKind Hive, string KeyPath, Action OnChanged) _entry;

        public Watcher( TestRegistryService service, (RegistryHiveKind Hive, string KeyPath, Action OnChanged) entry )
        {
            this._service = service;
            this._entry = entry;
        }

        public void Dispose() => this._service._watchers.Remove( this._entry );
    }

    /// <summary>
    /// Gets the key at a given path, creating it and its ancestors, so that a test can seed the hive with the values
    /// that another version of the product would have written.
    /// </summary>
    public IRegistryKey GetOrCreateKey( RegistryHiveKind hive, string keyPath )
        => this._hives[hive].OpenPath( keyPath, true )!;

    /// <summary>
    /// Determines whether a key exists, without creating it.
    /// </summary>
    public bool KeyExists( RegistryHiveKind hive, string keyPath ) => this._hives[hive].OpenPath( keyPath, false ) != null;

    private sealed class TestRegistryKey : IRegistryKey
    {
        /// <summary>
        /// The character that separates the segments of a key path.
        /// </summary>
        private const char _separator = '\\';

        private readonly Dictionary<string, object> _values = new( StringComparer.OrdinalIgnoreCase );
        private readonly Dictionary<string, TestRegistryKey> _subKeys = new( StringComparer.OrdinalIgnoreCase );
        private readonly TestRegistryService? _service;

        public TestRegistryKey( string displayPath, TestRegistryService? service = null )
        {
            this.DisplayPath = displayPath;
            this._service = service;
        }

        public string DisplayPath { get; }

        /// <summary>
        /// Walks a path of sub-keys separated by backslashes, as the registry API does when it is given a path
        /// rather than a name.
        /// </summary>
        public TestRegistryKey? OpenPath( string keyPath, bool create )
        {
            var current = this;

            foreach ( var segment in keyPath.Split( [_separator], StringSplitOptions.RemoveEmptyEntries ) )
            {
                if ( !current._subKeys.TryGetValue( segment, out var next ) )
                {
                    if ( !create )
                    {
                        return null;
                    }

                    next = new TestRegistryKey( current.DisplayPath + "\\" + segment, current._service );
                    current._subKeys.Add( segment, next );
                }

                current = next;
            }

            return current;
        }

        public object? GetValue( string name ) => this._values.TryGetValue( name, out var value ) ? value : null;

        public void SetStringValue( string name, string value ) => this.SetValue( name, value );

        public void SetDWordValue( string name, int value ) => this.SetValue( name, value );

        public void SetQWordValue( string name, long value ) => this.SetValue( name, value );

        private void SetValue( string name, object value )
        {
            if ( this._service is { FailWrites: true } )
            {
                throw new UnauthorizedAccessException( $@"'{this.DisplayPath}\{name}' cannot be written." );
            }

            this._values[name] = value;
            this._service?.CountWrite();
        }

        public void DeleteValue( string name ) => this._values.Remove( name );

        public IReadOnlyList<string> GetValueNames() => this._values.Keys.ToList();

        public IReadOnlyList<string> GetSubKeyNames() => this._subKeys.Keys.ToList();

        public IRegistryKey? OpenSubKey( string name, bool writable = false ) => this.OpenPath( name, false );

        public IRegistryKey? CreateSubKey( string name ) => this.OpenPath( name, true );

        public void DeleteSubKeyTree( string name ) => this._subKeys.Remove( name );

        public void Dispose() { }
    }
}
