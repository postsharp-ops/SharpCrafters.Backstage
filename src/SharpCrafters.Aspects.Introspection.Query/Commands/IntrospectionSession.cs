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
/// The scope of a command, resolved to the build outputs of its projects, and their merged model
/// (Documentation/future/Introspection-Cli.md of PostSharp, sections 5.6 and 5.7).
/// </summary>
internal sealed class IntrospectionSession
{
    private IntrospectionSession( IReadOnlyList<IntrospectionTarget> targets, IReadOnlyDictionary<IntrospectionTarget, IntrospectionModel> models, QueryContext context )
    {
        this.Targets = targets;
        this.Models = models;
        this.Context = context;
    }

    /// <summary>
    /// Gets the targets whose files the command read.
    /// </summary>
    public IReadOnlyList<IntrospectionTarget> Targets { get; }

    /// <summary>
    /// Gets the model of each target, before the merge.
    /// </summary>
    public IReadOnlyDictionary<IntrospectionTarget, IntrospectionModel> Models { get; }

    public QueryContext Context { get; }

    /// <summary>
    /// Opens the scope of a command.
    /// </summary>
    /// <param name="options">The options of the tool.</param>
    /// <param name="settings">The settings of the command.</param>
    /// <param name="sourceFile">The full path of a source file, for a query that needs only the project whose directory holds it,
    /// such as <c>show declaration</c> with a source position; or <c>null</c> to read every project of the scope.</param>
    /// <remarks>
    /// In a batch, every query reads the session of the whole scope, which the first query opens
    /// (<see cref="IntrospectionSessionCache"/>): a source file does not restrict it.
    /// </remarks>
    public static IntrospectionSession Open( IntrospectionCommandOptions options, IntrospectionCommandSettings settings, string? sourceFile = null )
        => options.SessionCache?.GetOrOpen( settings, () => OpenCore( options, settings, null ) ) ?? OpenCore( options, settings, sourceFile );

    /// <summary>
    /// Gets the targets of the scope whose file exists, and warns about the targets whose file is missing or stale.
    /// </summary>
    public static IReadOnlyList<IntrospectionTarget> GetTargets( IntrospectionCommandOptions options, IntrospectionCommandSettings settings )
        => options.SessionCache != null ? Open( options, settings ).Targets : LocateTargets( options, settings );

    /// <summary>
    /// Gets the model of one target, before the merge with the other targets.
    /// </summary>
    public static IntrospectionModel GetModel( IntrospectionCommandOptions options, IntrospectionCommandSettings settings, IntrospectionTarget target )
        => options.SessionCache != null && Open( options, settings ).Models.TryGetValue( target, out var model ) ? model : LoadModel( options, target );

    private static IntrospectionSession OpenCore( IntrospectionCommandOptions options, IntrospectionCommandSettings settings, string? sourceFile )
    {
        var provider = options.Provider;
        var existing = LocateTargets( options, settings, sourceFile );
        var models = new Dictionary<IntrospectionTarget, IntrospectionModel>();

        foreach ( var target in existing )
        {
            models.Add( target, LoadModel( options, target ) );
        }

        var (model, provenance) = models.Count == 1
            ? (models[existing[0]], ModelMerger.Single( existing[0] ))
            : ModelMerger.Merge( existing.Select( t => (t, models[t]) ).ToList() );

        return new IntrospectionSession( existing, models, new QueryContext( model, provenance, provider, options.WorkingDirectory ) );
    }

    private static IntrospectionModel LoadModel( IntrospectionCommandOptions options, IntrospectionTarget target )
    {
        try
        {
            return ModelIndex.Load( target, options.Provider, options.CacheDirectory );
        }
        catch ( IntrospectionFormatException e )
        {
            throw new IntrospectionCommandException( $"The file '{target.FilePath}' is malformed: {e.Message}", IntrospectionExitCodes.Error );
        }
    }

    /// <summary>
    /// Gets the targets of the scope whose file exists, and warns about the targets whose file is missing or stale.
    /// </summary>
    /// <param name="options">The options of the tool.</param>
    /// <param name="settings">The settings of the command.</param>
    /// <param name="sourceFile">The full path of a source file, to restrict the scope to the project whose directory holds it, or
    /// <c>null</c>.</param>
    /// <exception cref="IntrospectionCommandException">No file exists, or the scope is invalid.</exception>
    public static IReadOnlyList<IntrospectionTarget> LocateTargets( IntrospectionCommandOptions options, IntrospectionCommandSettings settings, string? sourceFile = null )
    {
        var provider = options.Provider;
        var (projectPaths, solutionPath) = ResolveScope( options.WorkingDirectory, settings );

        if ( sourceFile != null && projectPaths.Count > 1 )
        {
            // The project whose directory holds the file, the deepest when they are nested.
            var owner = projectPaths.Where( p => IsUnder( sourceFile, Path.GetDirectoryName( p )! ) ).OrderByDescending( p => Path.GetDirectoryName( p )!.Length ).FirstOrDefault();

            if ( owner != null )
            {
                projectPaths = [owner];
            }
        }

        var projects = new List<IntrospectionProject>();

        using ( var locator = new ProjectLocator(
                   provider,
                   settings.Configuration,
                   settings.Framework,
                   new EvaluationCache( options.CacheDirectory ),
                   solutionPath,
                   m => IntrospectionConsole.WriteWarning( options, settings.Color, m ) ) )
        {
            projects.AddRange( projectPaths.Select( locator.Locate ) );
        }

        // The projects of the other product, and those that were never restored, are not of the scope.
        var ofProduct = projects.Where( p => p.Targets.Count > 0 ).ToList();
        var framework = settings.Framework != null ? $" for the target framework {settings.Framework}" : "";

        if ( ofProduct.Count == 0 )
        {
            var project = projects.Count == 1 ? projects[0] : null;

            // A framework that the project does not have is not a sign that the project does not use the product.
            if ( project != null && settings.Framework != null && project.TargetFrameworks.Count > 0
                 && !project.TargetFrameworks.Contains( settings.Framework, StringComparer.OrdinalIgnoreCase ) )
            {
                throw new IntrospectionCommandException(
                    $"The project '{project.Name}' does not target {settings.Framework}: its target frameworks are {string.Join( ", ", project.TargetFrameworks )}.",
                    IntrospectionExitCodes.Error );
            }

            throw new IntrospectionCommandException(
                project == null
                    ? $"No project of the solution sets {provider.FileProperty}{framework}: none uses {provider.ProductName}, or they were never restored."
                    : project.IsEvaluated
                        ? $"The project '{project.Name}' does not set {provider.FileProperty}{framework}: it does not use {provider.ProductName}, or it was never restored."
                        : $"No {provider.FileExtension} file was found under the build outputs of '{project.Name}'{framework}. {provider.MissingFileHint}",
                IntrospectionExitCodes.NoFile );
        }

        var targets = ofProduct.SelectMany( p => p.Targets ).ToList();
        var missing = targets.Where( t => !t.Exists ).ToList();
        var existing = targets.Where( t => t.Exists ).ToList();

        if ( existing.Count == 0 )
        {
            throw new IntrospectionCommandException(
                (missing.Count == 1
                    ? $"The file '{missing[0].FilePath}' of '{missing[0].Project.Name}' ({missing[0].Configuration}, {missing[0].TargetFramework}) does not exist."
                    : "No project of the scope has a file: "
                      + string.Join( ", ", missing.Select( t => $"{t.Project.Name} ({t.Configuration}, {t.TargetFramework})" ) ) + ".")
                + " " + provider.MissingFileHint,
                IntrospectionExitCodes.NoFile );
        }

        foreach ( var target in missing )
        {
            IntrospectionConsole.WriteWarning(
                options,
                settings.Color,
                $"the file of '{target.Project.Name}' ({target.Configuration}, {target.TargetFramework}) does not exist, so the result leaves it out. {provider.MissingFileHint}" );
        }

        foreach ( var target in existing.Where( t => t.IsStale ) )
        {
            IntrospectionConsole.WriteWarning(
                options,
                settings.Color,
                $"the file of '{target.Project.Name}' ({target.TargetFramework}) is older than a source file: it may not describe the current source. Build the project to refresh it." );
        }

        return existing;
    }

    /// <summary>
    /// Gets the targets of the project whose directory holds a source file, the deepest when the project directories are nested.
    /// </summary>
    public static IReadOnlyList<IntrospectionTarget> GetTargetsOfSourceFile( IReadOnlyList<IntrospectionTarget> targets, string file )
    {
        var owner = targets.Where( t => IsUnder( file, t.Project.Directory ) ).OrderByDescending( t => t.Project.Directory.Length ).FirstOrDefault();

        return owner == null ? [] : targets.Where( t => t.Project == owner.Project ).ToList();
    }

    /// <summary>
    /// Resolves the projects of the scope. Without <c>--project</c> or <c>--solution</c>, as the <c>dotnet</c> command line does:
    /// the current directory must hold exactly one project or solution file.
    /// </summary>
    internal static (IReadOnlyList<string> Projects, string? Solution) ResolveScope( string workingDirectory, IntrospectionCommandSettings settings )
    {
        if ( settings.Project != null )
        {
            return ([ResolvePath( workingDirectory, settings.Project, IsProjectFile, "project" )], null);
        }

        if ( settings.Solution != null )
        {
            var solution = ResolvePath( workingDirectory, settings.Solution, IsSolutionFile, "solution" );

            return (ReadSolution( solution ), solution);
        }

        var projects = Directory.EnumerateFiles( workingDirectory ).Where( IsProjectFile ).ToList();
        var solutions = Directory.EnumerateFiles( workingDirectory ).Where( IsSolutionFile ).ToList();
        var candidates = projects.Concat( solutions ).OrderBy( f => f, StringComparer.Ordinal ).ToList();

        if ( candidates.Count == 1 )
        {
            return projects.Count == 1 ? (projects, null) : (ReadSolution( solutions[0] ), solutions[0]);
        }

        throw new IntrospectionCommandException(
            candidates.Count == 0
                ? $"The directory '{workingDirectory}' holds no project or solution file: give one with --project or --solution."
                : $"The directory '{workingDirectory}' holds several project or solution files, so the scope must be given with --project or --solution: "
                  + string.Join( ", ", candidates.Select( Path.GetFileName ) ) + ".",
            IntrospectionExitCodes.Error );
    }

    private static IReadOnlyList<string> ReadSolution( string solution )
    {
        var projects = SolutionReader.GetProjects( solution );

        return projects.Count > 0
            ? projects
            : throw new IntrospectionCommandException( $"The solution '{solution}' has no project.", IntrospectionExitCodes.Error );
    }

    // A file, or a directory that holds exactly one file of the kind.
    private static string ResolvePath( string workingDirectory, string value, Func<string, bool> isOfKind, string kind )
    {
        var path = Path.GetFullPath( Path.Combine( workingDirectory, value ) );

        if ( File.Exists( path ) )
        {
            return isOfKind( path ) ? path : throw new IntrospectionCommandException( $"'{value}' is not a {kind} file.", IntrospectionExitCodes.Error );
        }

        if ( !Directory.Exists( path ) )
        {
            throw new IntrospectionCommandException( $"The {kind} '{value}' does not exist.", IntrospectionExitCodes.Error );
        }

        var files = Directory.EnumerateFiles( path ).Where( isOfKind ).OrderBy( f => f, StringComparer.Ordinal ).ToList();

        return files.Count == 1
            ? files[0]
            : throw new IntrospectionCommandException(
                files.Count == 0
                    ? $"The directory '{value}' holds no {kind} file."
                    : $"The directory '{value}' holds several {kind} files: {string.Join( ", ", files.Select( Path.GetFileName ) )}. Give one.",
                IntrospectionExitCodes.Error );
    }

    private static bool IsUnder( string file, string directory )
        => file.StartsWith( directory.TrimEnd( Path.DirectorySeparatorChar ) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase );

    internal static bool IsProjectFile( string path ) => Path.GetExtension( path ) is { Length: > 4 } extension && extension.EndsWith( "proj", StringComparison.OrdinalIgnoreCase );

    internal static bool IsSolutionFile( string path ) => Path.GetExtension( path ) is ".sln" or ".slnx";
}
