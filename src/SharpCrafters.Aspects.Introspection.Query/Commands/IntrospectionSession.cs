// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Aspects.Introspection.Query.Projects;
using SharpCrafters.Aspects.Introspection.Query.Querying;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SharpCrafters.Aspects.Introspection.Query.Commands;

/// <summary>
/// The scope of a command, resolved to the build output of one project, and its model
/// (Documentation/future/Introspection-Cli.md of PostSharp, sections 5.6 and 5.7).
/// </summary>
internal sealed class IntrospectionSession
{
    private IntrospectionSession( IntrospectionTarget target, QueryContext context )
    {
        this.Target = target;
        this.Context = context;
    }

    public IntrospectionTarget Target { get; }

    public QueryContext Context { get; }

    public IReadOnlyList<IntrospectionTarget> Targets => [this.Target];

    public static IntrospectionSession Open( IntrospectionCommandOptions options, IntrospectionCommandSettings settings )
    {
        var provider = options.Provider;
        var projectPath = ResolveProject( options.WorkingDirectory, settings );
        var project = ProjectLocator.Locate( projectPath, provider, settings.Configuration, settings.Framework, options.ErrorOutput.WriteLine );

        // A framework that the project does not have is not a sign that the project does not use the product.
        if ( project.Targets.Count == 0 && settings.Framework != null && project.TargetFrameworks.Count > 0
             && !project.TargetFrameworks.Contains( settings.Framework, StringComparer.OrdinalIgnoreCase ) )
        {
            throw new IntrospectionCommandException(
                $"The project '{project.Name}' does not target {settings.Framework}: its target frameworks are {string.Join( ", ", project.TargetFrameworks )}.",
                IntrospectionExitCodes.Error );
        }

        if ( project.Targets.Count == 0 )
        {
            var framework = settings.Framework != null ? $" for the target framework {settings.Framework}" : "";

            throw new IntrospectionCommandException(
                project.IsEvaluated
                    ? $"The project '{project.Name}' does not set {provider.FileProperty}{framework}: it does not use {provider.ProductName}, or it was never restored."
                    : $"No {provider.FileExtension} file was found under the build outputs of '{project.Name}'{framework}. {provider.MissingFileHint}",
                IntrospectionExitCodes.NoFile );
        }

        // Several target frameworks are queried one at a time until their files are merged.
        if ( project.Targets.Count > 1 )
        {
            throw new IntrospectionCommandException(
                $"The project '{project.Name}' has several target frameworks ({string.Join( ", ", project.Targets.Select( t => t.TargetFramework ) )}): "
                + "choose one with -f.",
                IntrospectionExitCodes.Error );
        }

        var target = project.Targets[0];

        if ( !target.Exists )
        {
            throw new IntrospectionCommandException(
                $"The file '{target.FilePath}' of '{project.Name}' ({target.Configuration}, {target.TargetFramework}) does not exist. {provider.MissingFileHint}",
                IntrospectionExitCodes.NoFile );
        }

        IntrospectionModel model;

        try
        {
            model = provider.Load( target );
        }
        catch ( IntrospectionFormatException e )
        {
            throw new IntrospectionCommandException( $"The file '{target.FilePath}' is malformed: {e.Message}", IntrospectionExitCodes.Error );
        }

        if ( target.IsStale )
        {
            options.ErrorOutput.WriteLine(
                $"warning: '{target.FilePath}' is older than a source file of '{project.Name}': it may not describe the current source. Build the project to refresh it." );
        }

        return new IntrospectionSession( target, new QueryContext( model, target, provider, provider.GetSourceLocations( target ), options.WorkingDirectory ) );
    }

    /// <summary>
    /// Resolves the project of the scope. Without <c>--project</c>, as the <c>dotnet</c> command line does: the current directory
    /// must hold exactly one project or solution file.
    /// </summary>
    internal static string ResolveProject( string workingDirectory, IntrospectionCommandSettings settings )
    {
        if ( settings.Solution != null )
        {
            throw new IntrospectionCommandException( "--solution is not supported yet: give a project with --project.", IntrospectionExitCodes.Error );
        }

        if ( settings.Project != null )
        {
            var path = Path.GetFullPath( Path.Combine( workingDirectory, settings.Project ) );

            if ( File.Exists( path ) )
            {
                return path;
            }

            if ( !Directory.Exists( path ) )
            {
                throw new IntrospectionCommandException( $"The project '{settings.Project}' does not exist.", IntrospectionExitCodes.Error );
            }

            return GetSingleFile( path, false );
        }

        return GetSingleFile( workingDirectory, true );
    }

    private static string GetSingleFile( string directory, bool acceptSolution )
    {
        var projects = Directory.EnumerateFiles( directory ).Where( IsProjectFile ).ToList();
        var solutions = acceptSolution ? Directory.EnumerateFiles( directory ).Where( IsSolutionFile ).ToList() : [];
        var candidates = projects.Concat( solutions ).OrderBy( f => f, StringComparer.Ordinal ).ToList();

        if ( candidates.Count == 1 && projects.Count == 1 )
        {
            return projects[0];
        }

        if ( candidates.Count == 1 )
        {
            throw new IntrospectionCommandException( "Solutions are not supported yet: give a project with --project.", IntrospectionExitCodes.Error );
        }

        var what = acceptSolution ? "project or solution file" : "project file";

        throw new IntrospectionCommandException(
            candidates.Count == 0
                ? $"The directory '{directory}' holds no {what}: give one with --project{(acceptSolution ? " or --solution" : "")}."
                : $"The directory '{directory}' holds several {what}s, so the scope must be given with --project{(acceptSolution ? " or --solution" : "")}: "
                  + string.Join( ", ", candidates.Select( Path.GetFileName ) ) + ".",
            IntrospectionExitCodes.Error );
    }

    internal static bool IsProjectFile( string path ) => Path.GetExtension( path ).EndsWith( "proj", StringComparison.OrdinalIgnoreCase ) && Path.GetExtension( path ).Length > 4;

    internal static bool IsSolutionFile( string path ) => Path.GetExtension( path ) is ".sln" or ".slnx";
}
