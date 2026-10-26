// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SharpCrafters.Aspects.Introspection.Query.Projects;

/// <summary>
/// Finds the targets of a project and the paths of their files: by evaluating the project with the MSBuild of a .NET SDK, or,
/// when no SDK is compatible with the runtime, by searching the build outputs (Documentation/future/Introspection-Cli.md of
/// PostSharp, section 5.7).
/// </summary>
internal static class ProjectLocator
{
    public static IntrospectionProject Locate(
        string projectPath,
        IntrospectionProvider provider,
        string? configuration,
        string? targetFramework,
        Action<string> warn )
    {
        if ( DotNetSdkLocator.EnsureRegistered() != null )
        {
            return Evaluate( projectPath, provider, configuration, targetFramework );
        }

        warn( $"No .NET SDK is compatible with the runtime of this process, so '{Path.GetFileName( projectPath )}' was not evaluated: its "
              + $"files were searched under its 'bin' directory and the 'artifacts/bin' directory of the repository instead." );

        return Search( projectPath, provider, configuration, targetFramework );
    }

    // In a method of its own, never inlined, so that no type of Microsoft.Build is loaded before the SDK is registered.
    [System.Runtime.CompilerServices.MethodImpl( System.Runtime.CompilerServices.MethodImplOptions.NoInlining )]
    private static IntrospectionProject Evaluate( string projectPath, IntrospectionProvider provider, string? configuration, string? targetFramework )
    {
        using var evaluator = new MSBuildProjectEvaluator();

        return evaluator.Evaluate( projectPath, provider, configuration, targetFramework );
    }

    /// <summary>
    /// Searches the files of a project in its build outputs: <c>bin/&lt;configuration&gt;/&lt;target framework&gt;[/&lt;runtime
    /// identifier&gt;]</c> under the project, and <c>artifacts/bin/&lt;project&gt;/&lt;configuration&gt;[_&lt;target
    /// framework&gt;[_&lt;runtime identifier&gt;]]</c> of the artifacts output layout, in the nearest directory above the project
    /// that has one.
    /// </summary>
    /// <remarks>
    /// Without an evaluation, the compiled source files are unknown: a file older than the project file is reported as stale, and
    /// a file older than a source file is not.
    /// </remarks>
    internal static IntrospectionProject Search( string projectPath, IntrospectionProvider provider, string? configuration, string? targetFramework )
    {
        var project = new IntrospectionProject( projectPath, false );
        var projectWriteTime = File.Exists( project.Path ) ? File.GetLastWriteTimeUtc( project.Path ) : (DateTime?) null;
        // The depth is the number of folders of the layout that the file is under: a file of a runtime identifier, or under publish or
        // ref, is deeper than the file of the build.
        var found = new List<(string File, string Configuration, string Framework, int Depth)>();

        // bin/<configuration>/<target framework>[/<runtime identifier>]/<file>.
        var bin = Path.Combine( project.Directory, "bin" );

        foreach ( var file in EnumerateFiles( bin, provider ) )
        {
            var parts = Path.GetRelativePath( bin, Path.GetDirectoryName( file )! ).Split( Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar );
            found.Add( (file, parts[0], parts.Length > 1 ? parts[1] : "", parts.Length) );
        }

        // artifacts/bin/<project>/<configuration>[_<target framework>[_<runtime identifier>]]/<file>, with a lower-case configuration.
        if ( FindArtifactsBin( project ) is { } artifacts )
        {
            foreach ( var file in EnumerateFiles( artifacts, provider ) )
            {
                var folders = Path.GetRelativePath( artifacts, Path.GetDirectoryName( file )! ).Split( Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar );
                var pivot = folders[0].Split( '_' );
                found.Add( (file, pivot[0], pivot.Length > 1 ? pivot[1] : "", pivot.Length + folders.Length - 1) );
            }
        }

        // One file per configuration and target framework: that of the build, rather than that of a runtime identifier or a publish.
        var targets = found.GroupBy( f => (f.Configuration.ToLowerInvariant(), f.Framework.ToLowerInvariant()) )
            .Select( g => g.OrderBy( f => f.Depth ).ThenBy( f => f.File, StringComparer.Ordinal ).First() );

        foreach ( var (file, fileConfiguration, framework, _) in targets )
        {
            if ( (targetFramework != null && !string.Equals( framework, targetFramework, StringComparison.OrdinalIgnoreCase ))
                 || (configuration != null && !string.Equals( fileConfiguration, configuration, StringComparison.OrdinalIgnoreCase )) )
            {
                continue;
            }

            project.TargetsList.Add(
                new IntrospectionTarget(
                    project,
                    framework,
                    fileConfiguration,
                    Path.GetFileNameWithoutExtension( file ),
                    file,
                    new Dictionary<string, string>(),
                    projectWriteTime ) );
        }

        return project;
    }

    private static IEnumerable<string> EnumerateFiles( string root, IntrospectionProvider provider )
        => Directory.Exists( root )
            ? Directory.EnumerateFiles( root, "*" + provider.FileExtension, SearchOption.AllDirectories ).OrderBy( f => f, StringComparer.Ordinal )
            : [];

    // The directory of the project in the artifacts output layout: artifacts/bin/<project> in the project directory or above it,
    // where the Directory.Build.props that sets UseArtifactsOutput is.
    private static string? FindArtifactsBin( IntrospectionProject project )
    {
        for ( var directory = new DirectoryInfo( project.Directory ); directory != null; directory = directory.Parent )
        {
            var candidate = Path.Combine( directory.FullName, "artifacts", "bin", project.Name );

            if ( Directory.Exists( candidate ) )
            {
                return candidate;
            }
        }

        return null;
    }
}
