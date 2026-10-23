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
                $"warning: {registration.Failure} The projects were therefore not evaluated: their files were searched under 'bin' and 'artifacts/bin' instead." );
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

    public void Dispose() => this._evaluator?.Dispose();
}
