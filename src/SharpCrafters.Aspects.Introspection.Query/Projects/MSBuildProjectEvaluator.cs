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

        // A project that is not SDK-style has one target framework, given by TargetFrameworkVersion, and is evaluated once: a
        // TargetFramework global property would mean nothing to it.
        if ( frameworks.Count == 0 && GetLegacyTargetFramework( outer ) is { } legacy )
        {
            evaluation.TargetFrameworks = [legacy];

            try
            {
                if ( targetFramework == null || string.Equals( legacy, targetFramework, StringComparison.OrdinalIgnoreCase ) )
                {
                    AddTarget( evaluation, outer, legacy, provider );
                }
            }
            finally
            {
                this._collection.UnloadProject( outer );
            }

            evaluation.Inputs = inputs.ToList();

            return evaluation;
        }

        this._collection.UnloadProject( outer );
        evaluation.TargetFrameworks = frameworks.ToList();

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
                AddTarget( evaluation, inner, framework, provider );
            }
            finally
            {
                this._collection.UnloadProject( inner );
            }
        }

        evaluation.Inputs = inputs.ToList();

        return evaluation;
    }

    private static void AddTarget( ProjectEvaluation evaluation, Project evaluated, string framework, IntrospectionProvider provider )
    {
        var file = GetPath( evaluated, provider.FileProperty );

        // A project without the property is not of the product, or was never restored.
        if ( file == null )
        {
            return;
        }

        evaluation.Targets.Add(
            new EvaluatedTarget
            {
                TargetFramework = framework,
                Configuration = evaluated.GetPropertyValue( "Configuration" ),
                AssemblyName = evaluated.GetPropertyValue( "AssemblyName" ),
                FilePath = file,
                AdditionalFiles = provider.AdditionalFileProperties
                    .Select( p => (Property: p, Path: GetPath( evaluated, p )) )
                    .Where( p => p.Path != null )
                    .ToDictionary( p => p.Property, p => p.Path!, StringComparer.OrdinalIgnoreCase ),
                SourceFiles = GetSourceFiles( evaluated )
            } );
    }

    // The short name of the target framework of a project that is not SDK-style, such as net472 for v4.7.2, or null.
    internal static string? GetLegacyTargetFramework( Project project )
        => ToShortFrameworkName( project.GetPropertyValue( "TargetFrameworkVersion" ) );

    internal static string? ToShortFrameworkName( string targetFrameworkVersion )
    {
        var version = targetFrameworkVersion.Trim().TrimStart( 'v', 'V' );

        return version.Length == 0 || !version.All( c => char.IsDigit( c ) || c == '.' ) ? null : "net" + version.Replace( ".", "", StringComparison.Ordinal );
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

        AddMissingImports( project, inputs );
    }

    // The imports that were skipped because their file does not exist, such as the targets of a NuGet package that is not in the
    // packages folder (obj/*.nuget.g.targets imports them under Exists conditions). They are inputs that the cache records as
    // absent: a restore that brings the package back rewrites no other input. The path of an import is expanded with the final
    // properties of the project, which are those of the import in the common case of a path that does not change afterwards.
    private static void AddMissingImports( Project project, HashSet<string> inputs )
    {
        var files = project.Imports.Select( i => i.ImportedProject ).Prepend( project.Xml );

        foreach ( var file in files )
        {
            foreach ( var element in file.Imports )
            {
                // An import of an SDK is resolved by the SDK resolver, not by its path.
                if ( !string.IsNullOrEmpty( element.Sdk ) )
                {
                    continue;
                }

                var path = project.ExpandString( element.Project ).Trim();

                if ( path.Length == 0 || path.IndexOfAny( ['*', '?', ';'] ) >= 0 )
                {
                    continue;
                }

                try
                {
                    path = Path.GetFullPath( Path.Combine( file.DirectoryPath, path ) );
                }
                catch ( Exception e ) when ( e is ArgumentException or NotSupportedException or PathTooLongException )
                {
                    continue;
                }

                if ( !File.Exists( path ) )
                {
                    inputs.Add( path );
                }
            }
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
