// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using Microsoft.Build.Locator;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace SharpCrafters.Aspects.Introspection.Query.Projects;

/// <summary>
/// A .NET SDK whose MSBuild the process loads.
/// </summary>
/// <param name="Version">The version of the SDK, as <c>dotnet --list-sdks</c> gives it, or an empty string when it is unknown.</param>
/// <param name="Directory">The directory of the SDK, which holds its MSBuild, or an empty string when it is unknown.</param>
public sealed record DotNetSdk( string Version, string Directory );

/// <summary>
/// The result of <see cref="DotNetSdkLocator.EnsureRegistered"/>: the SDK whose MSBuild the process uses, or why there is none.
/// </summary>
/// <param name="Sdk">The SDK, or <c>null</c> when none could be registered.</param>
/// <param name="Failure">Why no SDK could be registered, or <c>null</c>.</param>
public sealed record DotNetSdkRegistration( DotNetSdk? Sdk, string? Failure );

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
public static class DotNetSdkLocator
{
    private static readonly Regex _sdkLineRegex = new(
        @"^(?<version>[0-9]+(?:\.[0-9]+)*(?:-[A-Za-z0-9\.]+)?)\s+\[(?<directory>[^\]]+)\]$",
        RegexOptions.CultureInvariant );

    // dotnet --list-sdks answers in well under a second; a muxer that hangs must not hang the command.
    private static readonly TimeSpan _listTimeout = TimeSpan.FromSeconds( 30 );

    private static readonly object _sync = new();
    private static DotNetSdkRegistration? _registration;

    /// <summary>
    /// Registers the MSBuild of a compatible .NET SDK, once per process.
    /// </summary>
    /// <returns>The registered SDK, or, when no SDK is compatible with the runtime of the process, when <c>dotnet</c> cannot be run,
    /// or when MSBuild is already loaded, why none was registered.</returns>
    public static DotNetSdkRegistration EnsureRegistered()
    {
        lock ( _sync )
        {
            return _registration ??= Register();
        }
    }

    private static DotNetSdkRegistration Register()
    {
        if ( MSBuildLocator.IsRegistered )
        {
            // Registered by the host, such as Metalama.Framework.Workspaces in the same process. MSBuildLocator does not say which
            // instance it registered: the SDK is found where MSBuild is loaded from.
            return new DotNetSdkRegistration( DescribeRegisteredByHost(), null );
        }

        if ( !MSBuildLocator.CanRegister )
        {
            return new DotNetSdkRegistration(
                null,
                "MSBuild assemblies are already loaded in this process, so the MSBuild of a .NET SDK cannot be registered." );
        }

        var (sdks, listFailure) = ListSdks( "dotnet", _listTimeout );

        if ( listFailure != null )
        {
            return new DotNetSdkRegistration( null, listFailure );
        }

        var rejections = new List<string>();
        var sdk = Choose( sdks, Environment.Version, RuntimeInformation.RuntimeIdentifier, rejections );

        if ( sdk == null )
        {
            return new DotNetSdkRegistration( null, DescribeNoCompatibleSdk( sdks, rejections, Environment.Version, RuntimeInformation.RuntimeIdentifier ) );
        }

        RegisterSdk( sdk );

        return new DotNetSdkRegistration( sdk, null );
    }

    /// <summary>
    /// Registers an SDK as <c>MSBuildLocator</c> registers the SDKs it discovers itself: with <see cref="MSBuildLocator.RegisterInstance"/>
    /// and the discovery type <see cref="DiscoveryType.DotNetSdk"/>, which sets <c>MSBUILD_EXE_PATH</c>, <c>MSBuildExtensionsPath</c> and
    /// <c>MSBuildSDKsPath</c>, so that the SDK resolvers and the imports find this SDK, as in a <c>dotnet build</c> of it.
    /// <see cref="MSBuildLocator.RegisterMSBuildPath(string)"/> sets none of them.
    /// </summary>
    private static void RegisterSdk( DotNetSdk sdk )
    {
        // The constructor of VisualStudioInstance is internal; Metalama.Framework.Workspaces calls it the same way.
        var constructor = typeof(VisualStudioInstance).GetConstructor(
            BindingFlags.NonPublic | BindingFlags.Instance,
            [typeof(string), typeof(string), typeof(Version), typeof(DiscoveryType)] );

        if ( constructor != null )
        {
            var instance = (VisualStudioInstance) constructor.Invoke(
                [$".NET SDK {sdk.Version}", sdk.Directory, ParseVersion( sdk.Version ) ?? new Version(), DiscoveryType.DotNetSdk] );

            MSBuildLocator.RegisterInstance( instance );
        }
        else
        {
            // A version of MSBuildLocator without this constructor: the same variables, set here.
            foreach ( var (name, value) in GetSdkEnvironment( sdk.Directory ) )
            {
                Environment.SetEnvironmentVariable( name, value );
            }

            MSBuildLocator.RegisterMSBuildPath( sdk.Directory );
        }
    }

    /// <summary>
    /// Gets the environment variables that <c>MSBuildLocator</c> sets for an SDK that it registers itself.
    /// </summary>
    internal static IReadOnlyList<(string Name, string Value)> GetSdkEnvironment( string sdkDirectory )
        =>
        [
            ("MSBUILD_EXE_PATH", Path.Combine( sdkDirectory, "MSBuild.dll" )),
            ("MSBuildExtensionsPath", sdkDirectory),
            ("MSBuildSDKsPath", Path.Combine( sdkDirectory, "Sdks" ))
        ];

    /// <summary>
    /// Describes the SDK that the host registered: the directory of the loaded <c>Microsoft.Build</c> assembly, or of
    /// <c>MSBUILD_EXE_PATH</c>. Its version is unknown when neither gives a directory.
    /// </summary>
    private static DotNetSdk DescribeRegisteredByHost()
    {
        var microsoftBuild = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault( a => a.GetName().Name == "Microsoft.Build" );
        var directory = microsoftBuild is { Location.Length: > 0 } ? Path.GetDirectoryName( microsoftBuild.Location ) : null;
        var msbuildPath = Environment.GetEnvironmentVariable( "MSBUILD_EXE_PATH" );

        directory ??= string.IsNullOrEmpty( msbuildPath ) ? null : Path.GetDirectoryName( msbuildPath );

        return directory == null ? new DotNetSdk( "", "" ) : FromDirectory( directory );
    }

    /// <summary>
    /// Describes the SDK of a directory: its version is the second line of its <c>.version</c> file, or the name of the directory.
    /// </summary>
    internal static DotNetSdk FromDirectory( string directory )
    {
        var versionFile = Path.Combine( directory, ".version" );
        var lines = File.Exists( versionFile ) ? File.ReadAllLines( versionFile ) : [];
        var version = lines.Length >= 2 && lines[1].Trim().Length > 0 ? lines[1].Trim() : Path.GetFileName( directory.TrimEnd( Path.DirectorySeparatorChar ) );

        return new DotNetSdk( version, directory );
    }

    /// <summary>
    /// Chooses the highest SDK whose major version does not exceed that of the runtime and that is built for the runtime
    /// identifier of the process.
    /// </summary>
    /// <param name="rejections">When given, receives why each SDK that is higher than the chosen one, or every SDK when none is
    /// chosen, was rejected.</param>
    internal static DotNetSdk? Choose( IEnumerable<DotNetSdk> sdks, Version runtimeVersion, string runtimeIdentifier, List<string>? rejections = null )
    {
        var candidates = sdks.Select( s => (Sdk: s, Version: ParseVersion( s.Version )) )
            .OrderByDescending( s => s.Version )
            .ThenBy( s => s.Sdk.Version, StringComparer.Ordinal )
            .ToList();

        foreach ( var (sdk, version) in candidates )
        {
            if ( version == null )
            {
                rejections?.Add( $"{sdk.Version} (not a version)" );
            }
            else if ( version.Major > runtimeVersion.Major )
            {
                rejections?.Add( $"{sdk.Version} (needs .NET {version.Major} or later)" );
            }
            else if ( GetRuntimeIdentifier( sdk.Directory ) is not { } sdkRuntimeIdentifier )
            {
                rejections?.Add( $"{sdk.Version} (no .version file in '{sdk.Directory}')" );
            }
            else if ( !string.Equals( sdkRuntimeIdentifier, runtimeIdentifier, StringComparison.OrdinalIgnoreCase ) )
            {
                rejections?.Add( $"{sdk.Version} (built for {sdkRuntimeIdentifier})" );
            }
            else
            {
                return sdk;
            }
        }

        return null;
    }

    /// <summary>
    /// Describes why no SDK is compatible with the runtime of the process.
    /// </summary>
    internal static string DescribeNoCompatibleSdk( IReadOnlyList<DotNetSdk> sdks, IReadOnlyList<string> rejections, Version runtimeVersion, string runtimeIdentifier )
        => $"No .NET SDK can be loaded by this process, which runs .NET {runtimeVersion} on {runtimeIdentifier}. "
           + (sdks.Count == 0 ? "'dotnet --list-sdks' lists no SDK." : $"The SDKs that 'dotnet --list-sdks' lists are: {string.Join( "; ", rejections )}.")
           + $" Install a .NET SDK of version {runtimeVersion.Major} or lower for {runtimeIdentifier}, or run the tool on a later runtime.";

    /// <summary>
    /// Parses the output of <c>dotnet --list-sdks</c>.
    /// </summary>
    internal static IReadOnlyList<DotNetSdk> ParseSdkList( string output )
        => output.Split( '\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries )
            .Select( l => _sdkLineRegex.Match( l ) )
            .Where( m => m.Success )
            .Select( m => new DotNetSdk( m.Groups["version"].Value, Path.Combine( m.Groups["directory"].Value, m.Groups["version"].Value ) ) )
            .ToList();

    /// <summary>
    /// Runs <c>&lt;fileName&gt; --list-sdks</c>, and gives its SDKs, or why it failed.
    /// </summary>
    /// <remarks>
    /// Both outputs are read while the process runs: a process whose error output fills its pipe would otherwise wait forever for a
    /// reader. A process that does not exit within <paramref name="timeout"/> is killed.
    /// </remarks>
    internal static (IReadOnlyList<DotNetSdk> Sdks, string? Failure) ListSdks( string fileName, TimeSpan timeout )
    {
        Process? process;

        try
        {
            process = Process.Start(
                new ProcessStartInfo( fileName, "--list-sdks" )
                {
                    RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true
                } );
        }
        catch ( Exception e ) when ( e is System.ComponentModel.Win32Exception or InvalidOperationException )
        {
            return ([], $"'{fileName} --list-sdks' could not be started, so no .NET SDK can be found: {e.Message}");
        }

        if ( process == null )
        {
            return ([], $"'{fileName} --list-sdks' could not be started, so no .NET SDK can be found.");
        }

        using ( process )
        {
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();

            if ( !process.WaitForExit( timeout ) )
            {
                try
                {
                    process.Kill( entireProcessTree: true );
                }
                catch ( InvalidOperationException )
                {
                    // The process exited in the meantime.
                }

                return ([], $"'{fileName} --list-sdks' did not exit within {timeout.TotalSeconds:0} seconds, so no .NET SDK can be found.");
            }

            // The asynchronous reads end with the process.
            var outputText = output.GetAwaiter().GetResult();
            var errorText = error.GetAwaiter().GetResult().Trim();

            return process.ExitCode == 0
                ? (ParseSdkList( outputText ), null)
                : ([], $"'{fileName} --list-sdks' failed with exit code {process.ExitCode}, so no .NET SDK can be found.{(errorText.Length > 0 ? " " + errorText : "")}");
        }
    }

    // The version without its prerelease label, which Version cannot parse.
    private static Version? ParseVersion( string version ) => Version.TryParse( version.Split( '-' )[0], out var parsed ) ? parsed : null;

    // The third line of the .version file of an SDK is its runtime identifier.
    private static string? GetRuntimeIdentifier( string directory )
    {
        var versionFile = Path.Combine( directory, ".version" );

        if ( !File.Exists( versionFile ) )
        {
            return null;
        }

        var lines = File.ReadAllLines( versionFile );

        return lines.Length >= 3 ? lines[2].Trim() : null;
    }
}
