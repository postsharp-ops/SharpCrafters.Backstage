// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using Microsoft.Build.Evaluation;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SharpCrafters.Aspects.Introspection.Query.Projects;

/// <summary>
/// Evaluates projects in the process, with the MSBuild that <see cref="DotNetSdkLocator"/> registered, and reads the paths of
/// the files from properties. It runs no target (Documentation/future/Introspection-Cli.md of PostSharp, section 5.7).
/// </summary>
/// <remarks>
/// A project is evaluated once to read its target frameworks, then once per target framework with the global properties
/// <c>TargetFramework</c> and <c>Configuration</c>. The evaluations share one <see cref="ProjectCollection"/>, so the
/// resolution of the SDK and the imported files are cached across projects.
/// </remarks>
internal sealed class MSBuildProjectEvaluator : IDisposable
{
    private readonly ProjectCollection _collection = new();

    public IntrospectionProject Evaluate( string projectPath, IntrospectionProvider provider, string? configuration, string? targetFramework )
    {
        var project = new IntrospectionProject( projectPath, true );
        var globalProperties = new Dictionary<string, string>( StringComparer.OrdinalIgnoreCase );

        if ( configuration != null )
        {
            globalProperties["Configuration"] = configuration;
        }

        var outer = this.Load( project.Path, globalProperties );
        var frameworks = GetTargetFrameworks( outer );
        this._collection.UnloadProject( outer );

        foreach ( var framework in frameworks )
        {
            if ( targetFramework != null && !string.Equals( framework, targetFramework, StringComparison.OrdinalIgnoreCase ) )
            {
                continue;
            }

            var inner = this.Load( project.Path, new Dictionary<string, string>( globalProperties ) { ["TargetFramework"] = framework } );

            try
            {
                var file = GetPath( inner, provider.FileProperty );

                // A project without the property is not of the product, or was never restored.
                if ( file == null )
                {
                    continue;
                }

                var additionalFiles = provider.AdditionalFileProperties
                    .Select( p => (Property: p, Path: GetPath( inner, p )) )
                    .Where( p => p.Path != null )
                    .ToDictionary( p => p.Property, p => p.Path!, StringComparer.OrdinalIgnoreCase );

                project.TargetsList.Add(
                    new IntrospectionTarget(
                        project,
                        framework,
                        inner.GetPropertyValue( "Configuration" ),
                        inner.GetPropertyValue( "AssemblyName" ),
                        file,
                        additionalFiles,
                        GetNewestSourceWriteTime( inner ) ) );
            }
            finally
            {
                this._collection.UnloadProject( inner );
            }
        }

        return project;
    }

    private Project Load( string path, Dictionary<string, string> globalProperties )
    {
        try
        {
            return new Project( path, globalProperties, null, this._collection );
        }
        catch ( Microsoft.Build.Exceptions.InvalidProjectFileException e )
        {
            throw new IntrospectionCommandException( $"The project '{path}' cannot be evaluated: {e.Message}", IntrospectionExitCodes.Error );
        }
    }

    private static IReadOnlyList<string> GetTargetFrameworks( Project project )
    {
        var frameworks = project.GetPropertyValue( "TargetFrameworks" );

        if ( string.IsNullOrWhiteSpace( frameworks ) )
        {
            frameworks = project.GetPropertyValue( "TargetFramework" );
        }

        return frameworks.Split( ';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries ).Distinct( StringComparer.OrdinalIgnoreCase ).ToList();
    }

    // A path property, relative to the directory of the project when it is not fully qualified, or null when it is empty.
    private static string? GetPath( Project project, string property )
    {
        var value = project.GetPropertyValue( property );

        return string.IsNullOrWhiteSpace( value ) ? null : Path.GetFullPath( Path.Combine( project.DirectoryPath, value.Trim() ) );
    }

    // The newest of the project file and the compiled source files: a file older than it may not describe the current source.
    private static DateTime GetNewestSourceWriteTime( Project project )
    {
        var newest = File.GetLastWriteTimeUtc( project.FullPath );

        foreach ( var item in project.GetItems( "Compile" ) )
        {
            var path = Path.GetFullPath( Path.Combine( project.DirectoryPath, item.EvaluatedInclude ) );

            // Generated sources under obj are rewritten by every build, after the file sometimes, and are not source.
            if ( path.StartsWith( Path.Combine( project.DirectoryPath, "obj" ) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase ) )
            {
                continue;
            }

            var time = File.GetLastWriteTimeUtc( path );

            if ( time > newest && File.Exists( path ) )
            {
                newest = time;
            }
        }

        return newest;
    }

    public void Dispose() => this._collection.Dispose();
}
