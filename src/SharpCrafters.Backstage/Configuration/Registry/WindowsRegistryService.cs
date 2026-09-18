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

        private void SetValue( string name, object value, RegistryValueKind kind )
        {
            try
            {
                this._key.SetValue( name, value, kind );
            }
            catch ( Exception e ) when ( IsRecoverable( e ) )
            {
                // The caller has no way to obtain the permission it lacks, and a build must not fail over it.
            }
        }

        public void DeleteValue( string name )
        {
            try
            {
                this._key.DeleteValue( name, false );
            }
            catch ( Exception e ) when ( IsRecoverable( e ) )
            {
                // As above.
            }
        }

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

        public IRegistryKey? CreateSubKey( string name )
        {
            try
            {
                var subKey = this._key.CreateSubKey( name, true );

                return subKey == null ? null : new WindowsRegistryKey( subKey, this.DisplayPath + "\\" + name );
            }
            catch ( Exception e ) when ( IsRecoverable( e ) )
            {
                return null;
            }
        }

        public void DeleteSubKeyTree( string name )
        {
            try
            {
                this._key.DeleteSubKeyTree( name, false );
            }
            catch ( Exception e ) when ( IsRecoverable( e ) )
            {
                // As above.
            }
        }

        public void Dispose() => this._key.Dispose();
    }
}
