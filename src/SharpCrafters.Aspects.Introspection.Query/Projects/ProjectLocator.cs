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
              + $"files were searched under 'bin' and 'artifacts/bin' instead." );

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
    /// Searches the files of a project under <c>bin</c> and <c>artifacts/bin</c>, as <c>&lt;configuration&gt;/&lt;target
    /// framework&gt;/&lt;assembly&gt;&lt;extension&gt;</c>.
    /// </summary>
    internal static IntrospectionProject Search( string projectPath, IntrospectionProvider provider, string? configuration, string? targetFramework )
    {
        var project = new IntrospectionProject( projectPath, false );
        var roots = new[] { Path.Combine( project.Directory, "bin" ), Path.Combine( project.Directory, "artifacts", "bin" ) };

        foreach ( var file in roots.Where( Directory.Exists )
                     .SelectMany( r => Directory.EnumerateFiles( r, "*" + provider.FileExtension, SearchOption.AllDirectories ) )
                     .OrderBy( f => f, StringComparer.Ordinal ) )
        {
            var framework = Path.GetFileName( Path.GetDirectoryName( file ) ) ?? "";
            var fileConfiguration = Path.GetFileName( Path.GetDirectoryName( Path.GetDirectoryName( file ) ) ) ?? "";

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
                    null ) );
        }

        return project;
    }
}
