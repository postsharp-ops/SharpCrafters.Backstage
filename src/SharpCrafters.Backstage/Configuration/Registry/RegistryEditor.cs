// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using JetBrains.Annotations;
using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SharpCrafters.Backstage.Configuration.Registry;

/// <summary>
/// Opens the registry editor of Windows on a given key, which is what <c>config edit</c> does for a configuration
/// object that the registry holds.
/// </summary>
[PublicAPI]
public static class RegistryEditor
{
    /// <summary>
    /// The key in which the registry editor remembers where it was last positioned, and which is the only way to
    /// tell it where to open: it has no command-line argument for a key.
    /// </summary>
    private const string _lastKeyKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Applets\Regedit";

    private const string _lastKeyValueName = "LastKey";

    /// <summary>
    /// Starts the registry editor positioned on a given key.
    /// </summary>
    /// <param name="keyPath">The full path of the key, including the name of the hive, for instance
    /// <c>HKEY_CURRENT_USER\Software\SharpCrafters\PostSharp 3</c>.</param>
    /// <remarks>
    /// The position is passed through the registry itself, because the registry editor takes no key on its command
    /// line. Failing to write it is not an error: the editor then opens where it was last left, which is still more
    /// useful than not opening at all.
    /// </remarks>
    public static void Open( string keyPath )
    {
        if ( !RuntimeInformation.IsOSPlatform( OSPlatform.Windows ) )
        {
            throw new PlatformNotSupportedException( "The registry editor exists on Windows only." );
        }

        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey( _lastKeyKeyPath );

            // Since Windows 10 the editor expects the path to start with the name of the computer node.
            key?.SetValue( _lastKeyValueName, @"Computer\" + keyPath, RegistryValueKind.String );
        }
        catch ( Exception e ) when ( e is UnauthorizedAccessException or System.Security.SecurityException or System.IO.IOException )
        {
            // The editor opens where it was last left.
        }

        // `-m` allows a second instance, so that the command works when the user already has the editor open.
        Process.Start( new ProcessStartInfo( "regedit.exe", "-m" ) { UseShellExecute = true } );
    }
}
