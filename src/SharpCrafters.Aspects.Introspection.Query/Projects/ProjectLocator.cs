// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;

namespace SharpCrafters.Aspects.Introspection.Query.Projects;

/// <summary>
/// Finds the targets of the projects of a command and the paths of their files: by evaluating the projects with the MSBuild of
/// a .NET SDK, through <see cref="EvaluationCache"/>, or, when no SDK is compatible with the runtime, by searching the build
/// outputs (Documentation/future/Introspection-Cli.md of PostSharp, section 5.7).
/// </summary>
internal sealed class ProjectLocator : IDisposable
{
    private readonly IntrospectionProvider _provider;
    private readonly string? _configuration;
    private readonly string? _targetFramework;
    private readonly EvaluationCache _cache;
    private readonly string? _solutionPath;
    private readonly Action<string> _warn;
    private bool _hasWarnedAboutSdk;

    // An MSBuildProjectEvaluator, held as IDisposable so that no type of Microsoft.Build is loaded with this class.
    private IDisposable? _evaluator;

    public ProjectLocator(
        IntrospectionProvider provider,
        string? configuration,
        string? targetFramework,
        EvaluationCache cache,
        string? solutionPath,
        Action<string> warn )
    {
        this._provider = provider;
        this._configuration = configuration;
        this._targetFramework = targetFramework;
        this._cache = cache;
        this._solutionPath = solutionPath;
        this._warn = warn;
    }

    public IntrospectionProject Locate( string projectPath )
    {
        var registration = DotNetSdkLocator.EnsureRegistered();

        if ( registration.Sdk is { } sdk )
        {
            // The SDK that a host registered may have no known version, which the cache entries depend on.
            if ( sdk.Version.Length == 0 )
            {
                return this.Evaluate( projectPath ).ToProject( true );
            }

            return this._cache.Evaluate(
                    projectPath,
                    this._provider,
                    this._configuration,
                    this._targetFramework,
                    sdk.Version,
                    this._solutionPath,
                    () => this.Evaluate( projectPath ) )
                .ToProject( true );
        }

        if ( !this._hasWarnedAboutSdk )
        {
            this._hasWarnedAboutSdk = true;

            this._warn(
                $"{registration.Failure} The projects were therefore not evaluated: their files were searched under "
                + "their 'bin' directories and the 'artifacts/bin' directory of the repository instead." );
        }

        return Search( projectPath, this._provider, this._configuration, this._targetFramework );
    }

    // Never inlined, so that no type of Microsoft.Build is loaded before the SDK is registered.
    [MethodImpl( MethodImplOptions.NoInlining )]
    private ProjectEvaluation Evaluate( string projectPath )
    {
        this._evaluator ??= new MSBuildProjectEvaluator();

        return ((MSBuildProjectEvaluator) this._evaluator).Evaluate( projectPath, this._provider, this._configuration, this._targetFramework );
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

    public void Dispose() => this._evaluator?.Dispose();
}
