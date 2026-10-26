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
/// resolution of the SDK and the imported files are cached across the projects of a command.
/// </remarks>
internal sealed class MSBuildProjectEvaluator : IDisposable
{
    private readonly ProjectCollection _collection = new();

    public ProjectEvaluation Evaluate( string projectPath, IntrospectionProvider provider, string? configuration, string? targetFramework )
    {
        var evaluation = new ProjectEvaluation { ProjectPath = Path.GetFullPath( projectPath ) };
        var inputs = new HashSet<string>( StringComparer.OrdinalIgnoreCase ) { evaluation.ProjectPath };
        var globalProperties = new Dictionary<string, string>( StringComparer.OrdinalIgnoreCase );

        if ( configuration != null )
        {
            globalProperties["Configuration"] = configuration;
        }

        var outer = this.Load( evaluation.ProjectPath, globalProperties );
        var frameworks = GetTargetFrameworks( outer );
        AddImports( outer, inputs );
        AddImportCandidates( outer, inputs );
        this._collection.UnloadProject( outer );

        foreach ( var framework in frameworks )
        {
            if ( targetFramework != null && !string.Equals( framework, targetFramework, StringComparison.OrdinalIgnoreCase ) )
            {
                continue;
            }

            var inner = this.Load( evaluation.ProjectPath, new Dictionary<string, string>( globalProperties ) { ["TargetFramework"] = framework } );

            try
            {
                AddImports( inner, inputs );

                var file = GetPath( inner, provider.FileProperty );

                // A project without the property is not of the product, or was never restored.
                if ( file == null )
                {
                    continue;
                }

                evaluation.Targets.Add(
                    new EvaluatedTarget
                    {
                        TargetFramework = framework,
                        Configuration = inner.GetPropertyValue( "Configuration" ),
                        AssemblyName = inner.GetPropertyValue( "AssemblyName" ),
                        FilePath = file,
                        AdditionalFiles = provider.AdditionalFileProperties
                            .Select( p => (Property: p, Path: GetPath( inner, p )) )
                            .Where( p => p.Path != null )
                            .ToDictionary( p => p.Property, p => p.Path!, StringComparer.OrdinalIgnoreCase ),
                        SourceFiles = GetSourceFiles( inner )
                    } );
            }
            finally
            {
                this._collection.UnloadProject( inner );
            }
        }

        evaluation.Inputs = inputs.ToList();

        return evaluation;
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

    private static void AddImports( Project project, HashSet<string> inputs )
    {
        foreach ( var import in project.Imports )
        {
            inputs.Add( import.ImportedProject.FullPath );
        }
    }

    // The files that the SDK would import if they existed, which are inputs although they do not exist yet: the cache records
    // them as absent, so that their appearance invalidates the evaluation. They are the files that the SDK searches above the
    // project, up to the first that exists, and those that the restore writes.
    private static void AddImportCandidates( Project project, HashSet<string> inputs )
    {
        foreach ( var name in new[] { "Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props" } )
        {
            for ( var directory = new DirectoryInfo( project.DirectoryPath ); directory != null; directory = directory.Parent )
            {
                var candidate = Path.Combine( directory.FullName, name );
                inputs.Add( candidate );

                if ( File.Exists( candidate ) )
                {
                    break;
                }
            }
        }

        var extensions = GetPath( project, "MSBuildProjectExtensionsPath" ) ?? Path.Combine( project.DirectoryPath, "obj" );
        var projectFile = Path.GetFileName( project.FullPath );
        inputs.Add( Path.Combine( extensions, projectFile + ".nuget.g.props" ) );
        inputs.Add( Path.Combine( extensions, projectFile + ".nuget.g.targets" ) );
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

    // The compiled source files. The generated sources under obj are rewritten by every build, sometimes after the file, and are
    // not source.
    private static List<string> GetSourceFiles( Project project )
    {
        var obj = Path.Combine( project.DirectoryPath, "obj" ) + Path.DirectorySeparatorChar;

        return project.GetItems( "Compile" )
            .Select( i => Path.GetFullPath( Path.Combine( project.DirectoryPath, i.EvaluatedInclude ) ) )
            .Where( p => !p.StartsWith( obj, StringComparison.OrdinalIgnoreCase ) )
            .Distinct( StringComparer.OrdinalIgnoreCase )
            .ToList();
    }

    public void Dispose() => this._collection.Dispose();
}
