// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using Microsoft.Build.Locator;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace SharpCrafters.Aspects.Introspection.Query.Projects;

/// <summary>
/// Chooses the .NET SDK whose MSBuild evaluates the projects, and registers it with <see cref="MSBuildLocator"/>
/// (Documentation/future/Introspection-Cli.md of PostSharp, section 5.7). Moved from <c>MSBuildInitializer</c> of
/// <c>Metalama.Framework.Workspaces</c>.
/// </summary>
/// <remarks>
/// <para>
/// The MSBuild of a .NET SDK can be loaded only by a runtime of the same major version or later, and of the same processor
/// architecture. The SDKs are listed with <c>dotnet --list-sdks</c>, because <see cref="MSBuildLocator"/> does not find
/// the SDKs that <c>dotnet-install.ps1</c> installs in containers. The highest SDK whose major version does not exceed that of
/// the runtime and whose <c>.version</c> file names the runtime identifier of the process is chosen. <c>global.json</c> is not
/// honoured: the commands read the files of a build, whatever SDK built them.
/// </para>
/// <para>
/// No type of <c>Microsoft.Build</c> may be loaded before the registration, so the code that uses them is in other classes.
/// </para>
/// </remarks>
public static partial class DotNetSdkLocator
{
    [GeneratedRegex( @"^(?<version>[0-9]+(?:\.[0-9]+)*(?:-[A-Za-z0-9\.]+)?)\s+\[(?<directory>[^\]]+)\]$", RegexOptions.CultureInvariant )]
    private static partial Regex SdkLineRegex();

    private static readonly TimeSpan _listTimeout = TimeSpan.FromSeconds( 30 );

    private static readonly object _sync = new();
    private static DotNetSdk? _registered;

    /// <summary>
    /// Registers the MSBuild of a compatible .NET SDK, once per process.
    /// </summary>
    /// <returns>The registered SDK, or <c>null</c> when no SDK is compatible with the runtime of the process, or when
    /// <c>dotnet</c> cannot be run.</returns>
    public static DotNetSdk? EnsureRegistered()
    {
        lock ( _sync )
        {
            if ( _registered != null )
            {
                return _registered;
            }

            if ( MSBuildLocator.IsRegistered )
            {
                // Registered by the host, such as Metalama.Framework.Workspaces in the same process.
                var instance = MSBuildLocator.QueryVisualStudioInstances().FirstOrDefault();
                _registered = new DotNetSdk( instance?.Version.ToString() ?? "", instance?.MSBuildPath ?? "" );

                return _registered;
            }

            var sdk = Choose( ListSdks(), Environment.Version, RuntimeInformation.RuntimeIdentifier );

            if ( sdk == null || !MSBuildLocator.CanRegister )
            {
                return null;
            }

            MSBuildLocator.RegisterMSBuildPath( sdk.Directory );
            _registered = sdk;

            return sdk;
        }
    }

    /// <summary>
    /// Chooses the highest SDK whose major version does not exceed that of the runtime and that is built for the runtime
    /// identifier of the process.
    /// </summary>
    internal static DotNetSdk? Choose( IEnumerable<DotNetSdk> sdks, Version runtimeVersion, string runtimeIdentifier )
        => sdks.Select( s => (Sdk: s, Version: ParseVersion( s.Version )) )
            .Where( s => s.Version != null && s.Version.Major <= runtimeVersion.Major )
            .OrderByDescending( s => s.Sdk.Version, SdkVersionComparer.Instance )
            .Select( s => s.Sdk )
            .FirstOrDefault( s => HasRuntimeIdentifier( s.Directory, runtimeIdentifier ) );

    /// <summary>
    /// Parses the output of <c>dotnet --list-sdks</c>.
    /// </summary>
    internal static IReadOnlyList<DotNetSdk> ParseSdkList( string output )
        => output.Split( '\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries )
            .Select( l => SdkLineRegex().Match( l ) )
            .Where( m => m.Success )
            .Select( m => new DotNetSdk( m.Groups["version"].Value, Path.Combine( m.Groups["directory"].Value, m.Groups["version"].Value ) ) )
            .ToList();

    private static IReadOnlyList<DotNetSdk> ListSdks()
    {
        try
        {
            using var process = Process.Start(
                new ProcessStartInfo( "dotnet", "--list-sdks" )
                {
                    RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true
                } );

            if ( process == null )
            {
                return [];
            }

            // Both streams are read while the process runs: a child that fills the pipe of an unread stream blocks forever. The reads
            // end before the process is disposed, which would otherwise fault them.
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();

            // dotnet --list-sdks answers in well under a second; a muxer that hangs must not hang the command.
            if ( !process.WaitForExit( _listTimeout ) )
            {
                try
                {
                    process.Kill( entireProcessTree: true );
                }
                catch ( InvalidOperationException )
                {
                    // It exited meanwhile.
                }

                WaitForReads( output, error );

                return [];
            }

            WaitForReads( output, error );

            return process.ExitCode == 0 && output.IsCompletedSuccessfully ? ParseSdkList( output.Result ) : [];
        }
        catch ( System.ComponentModel.Win32Exception )
        {
            // dotnet is not on the path.
            return [];
        }
    }

    // Waits for the reads of the streams of a process, which end when it exits or is killed, and observes their exceptions.
    internal static void WaitForReads( params Task[] reads )
    {
        try
        {
            Task.WaitAll( reads, TimeSpan.FromSeconds( 5 ) );
        }
        catch ( AggregateException )
        {
            // A read that failed gives no output.
        }
    }

    // The version without its prerelease label, which Version cannot parse.
    private static Version? ParseVersion( string version ) => Version.TryParse( version.Split( '-' )[0], out var parsed ) ? parsed : null;

    // The third line of the .version file of an SDK is its runtime identifier.
    private static bool HasRuntimeIdentifier( string directory, string runtimeIdentifier )
    {
        var versionFile = Path.Combine( directory, ".version" );

        if ( !File.Exists( versionFile ) )
        {
            return false;
        }

        var lines = File.ReadAllLines( versionFile );

        return lines.Length >= 3 && string.Equals( lines[2].Trim(), runtimeIdentifier, StringComparison.OrdinalIgnoreCase );
    }
}
