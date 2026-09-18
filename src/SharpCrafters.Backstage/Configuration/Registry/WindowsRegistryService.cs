// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;

namespace SharpCrafters.Backstage.Configuration.Registry;

/// <summary>
/// Reads and writes the registry of the machine.
/// </summary>
/// <remarks>
/// <para>
/// Every key is opened in the <see cref="RegistryView.Registry32"/> view, which is what PostSharp 2026.0 does and
/// what the two versions sharing their settings depends on. <c>HKEY_CURRENT_USER\Software</c> is not redirected, so
/// the view changes nothing there, but <c>HKEY_LOCAL_MACHINE\SOFTWARE</c> is: the machine-wide keys of PostSharp are
/// physically under <c>WOW6432Node</c>, and opening the native view would silently find none of them.
/// </para>
/// <para>
/// Every operation that the caller cannot prevent from failing — a key it may not read, a value an administrator has
/// locked — is reported by returning <see langword="null"/> rather than by throwing, as PostSharp 2026.0 does.
/// Licensing must not fail a build because a registry key is unreadable.
/// </para>
/// </remarks>
#pragma warning disable CA1416 // The registry is reached only where IsSupported reports one.
internal sealed class WindowsRegistryService : IRegistryService
{
    public static WindowsRegistryService Instance { get; } = new();

    private WindowsRegistryService() { }

    public bool IsSupported => RuntimeInformation.IsOSPlatform( OSPlatform.Windows );

    private static RegistryKey OpenBaseKey( RegistryHiveKind hive )
        => RegistryKey.OpenBaseKey(
            hive == RegistryHiveKind.LocalMachine ? RegistryHive.LocalMachine : RegistryHive.CurrentUser,
            RegistryView.Registry32 );

    public IRegistryKey? OpenKey( RegistryHiveKind hive, string keyPath, bool writable = false )
    {
        if ( !this.IsSupported )
        {
            return null;
        }

        try
        {
            using var baseKey = OpenBaseKey( hive );
            var key = baseKey.OpenSubKey( keyPath, writable );

            return key == null ? null : new WindowsRegistryKey( key, GetDisplayPathCore( hive, keyPath ) );
        }
        catch ( Exception e ) when ( IsRecoverable( e ) )
        {
            return null;
        }
    }

    public IRegistryKey? CreateKey( RegistryHiveKind hive, string keyPath )
    {
        if ( !this.IsSupported )
        {
            return null;
        }

        try
        {
            using var baseKey = OpenBaseKey( hive );
            var key = baseKey.CreateSubKey( keyPath, true );

            return key == null ? null : new WindowsRegistryKey( key, GetDisplayPathCore( hive, keyPath ) );
        }
        catch ( Exception e ) when ( IsRecoverable( e ) )
        {
            return null;
        }
    }

    public string GetDisplayPath( RegistryHiveKind hive, string keyPath ) => GetDisplayPathCore( hive, keyPath );

    public IDisposable? WatchChanges( RegistryHiveKind hive, string keyPath, Action onChanged )
    {
        if ( !this.IsSupported )
        {
            return null;
        }

        try
        {
            // Created rather than opened, because a key cannot be watched before it exists and the product would
            // otherwise notice nothing until its next start on a machine where it has never run. The keys are the
            // ones this product owns, and an empty one is harmless; a write creates them in any case.
            using var baseKey = OpenBaseKey( hive );
            var key = baseKey.CreateSubKey( keyPath, false );

            if ( key == null )
            {
                return null;
            }

            return RegistryChangeWatcher.Create( key, onChanged );
        }
        catch ( Exception e ) when ( IsRecoverable( e ) )
        {
            return null;
        }
    }

    private static string GetDisplayPathCore( RegistryHiveKind hive, string keyPath )
        => (hive == RegistryHiveKind.LocalMachine ? "HKEY_LOCAL_MACHINE" : "HKEY_CURRENT_USER") + "\\" + keyPath;

    /// <summary>
    /// Determines whether an exception is one that the registry raises for a reason the caller cannot act upon, and
    /// which must therefore be swallowed rather than reported.
    /// </summary>
    internal static bool IsRecoverable( Exception e )
        => e is SecurityException or UnauthorizedAccessException or IOException or ObjectDisposedException;

    private sealed class WindowsRegistryKey : IRegistryKey
    {
        private readonly RegistryKey _key;

        public WindowsRegistryKey( RegistryKey key, string displayPath )
        {
            this._key = key;
            this.DisplayPath = displayPath;
        }

        public string DisplayPath { get; }

        public object? GetValue( string name )
        {
            try
            {
                return this._key.GetValue( name );
            }
            catch ( Exception e ) when ( IsRecoverable( e ) )
            {
                return null;
            }
        }

        public void SetStringValue( string name, string value ) => this.SetValue( name, value, RegistryValueKind.String );

        public void SetDWordValue( string name, int value ) => this.SetValue( name, value, RegistryValueKind.DWord );

        public void SetQWordValue( string name, long value ) => this.SetValue( name, value, RegistryValueKind.QWord );

        // A failed write is not swallowed here, unlike a failed read. A read that fails has an answer that means
        // something — the value is absent — and every caller already handles it. A write that fails has none: the
        // key keeps the value it had, and a caller told nothing goes on to announce a change that did not happen.
        // RegistryConfigurationManager.UpdateWithinLock catches these and returns WriteFailed, which is the outcome
        // it already defines for exactly this, so the exception reaches the one place that can report it and no
        // build fails over it either.
        private void SetValue( string name, object value, RegistryValueKind kind ) => this._key.SetValue( name, value, kind );

        /// <remarks>As <see cref="SetValue"/>: a failed delete is a failed write and is reported as one.</remarks>
        public void DeleteValue( string name ) => this._key.DeleteValue( name, false );

        public IReadOnlyList<string> GetValueNames()
        {
            try
            {
                return this._key.GetValueNames();
            }
            catch ( Exception e ) when ( IsRecoverable( e ) )
            {
                return [];
            }
        }

        public IReadOnlyList<string> GetSubKeyNames()
        {
            try
            {
                return this._key.GetSubKeyNames();
            }
            catch ( Exception e ) when ( IsRecoverable( e ) )
            {
                return [];
            }
        }

        public IRegistryKey? OpenSubKey( string name, bool writable = false )
        {
            try
            {
                var subKey = this._key.OpenSubKey( name, writable );

                return subKey == null ? null : new WindowsRegistryKey( subKey, this.DisplayPath + "\\" + name );
            }
            catch ( Exception e ) when ( IsRecoverable( e ) )
            {
                return null;
            }
        }

        /// <remarks>
        /// As <see cref="SetValue"/>. This is the other half of it: a sub-key that cannot be created is a write that
        /// did not happen, and returning <see langword="null"/> here would put the silence back that removing the
        /// catch from the setters took away, because the schemas reach a sub-key before they write into it.
        /// </remarks>
        public IRegistryKey? CreateSubKey( string name )
        {
            var subKey = this._key.CreateSubKey( name, true );

            return subKey == null ? null : new WindowsRegistryKey( subKey, this.DisplayPath + "\\" + name );
        }

        /// <remarks>As <see cref="SetValue"/>: a failed delete is a failed write and is reported as one.</remarks>
        public void DeleteSubKeyTree( string name ) => this._key.DeleteSubKeyTree( name, false );

        public void Dispose() => this._key.Dispose();
    }
}
#pragma warning restore CA1416
