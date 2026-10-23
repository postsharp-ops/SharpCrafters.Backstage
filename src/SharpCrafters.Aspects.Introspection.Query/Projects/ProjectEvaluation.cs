// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SharpCrafters.Aspects.Introspection.Query.Projects;

/// <summary>
/// What the evaluation of a project gives, which <see cref="EvaluationCache"/> stores: its targets, and the files whose
/// change invalidates it.
/// </summary>
internal sealed class ProjectEvaluation
{
    public string ProjectPath { get; set; } = "";

    public List<EvaluatedTarget> Targets { get; set; } = [];

    // The project file and every file that its evaluation imported (Directory.Build.props, obj/*.nuget.g.props and the others).
    public List<string> Inputs { get; set; } = [];

    public IntrospectionProject ToProject( bool isEvaluated )
    {
        var project = new IntrospectionProject( this.ProjectPath, isEvaluated );
        var projectWriteTime = File.GetLastWriteTimeUtc( this.ProjectPath );

        foreach ( var target in this.Targets )
        {
            // The newest of the project file and the compiled source files, at the time of the command, not of the evaluation.
            var newest = target.SourceFiles.Where( File.Exists ).Select( File.GetLastWriteTimeUtc ).Append( projectWriteTime ).Max();

            project.TargetsList.Add(
                new IntrospectionTarget(
                    project,
                    target.TargetFramework,
                    target.Configuration,
                    target.AssemblyName,
                    target.FilePath,
                    target.AdditionalFiles,
                    isEvaluated ? newest : null ) );
        }

        return project;
    }
}

internal sealed class EvaluatedTarget
{
    public string TargetFramework { get; set; } = "";

    public string Configuration { get; set; } = "";

    public string AssemblyName { get; set; } = "";

    public string FilePath { get; set; } = "";

    public Dictionary<string, string> AdditionalFiles { get; set; } = new( StringComparer.OrdinalIgnoreCase );

    // The compiled source files, outside of obj, whose write times tell whether the file is stale.
    public List<string> SourceFiles { get; set; } = [];
}
