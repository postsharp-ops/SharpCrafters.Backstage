// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using System;
using System.Collections.Generic;
using System.IO;

namespace SharpCrafters.Aspects.Introspection.Query.Projects;

/// <summary>
/// A project of the scope of a command, with the build outputs of its target frameworks.
/// </summary>
public sealed class IntrospectionProject
{
    internal IntrospectionProject( string path, bool isEvaluated )
    {
        this.Path = System.IO.Path.GetFullPath( path );
        this.Name = System.IO.Path.GetFileNameWithoutExtension( path );
        this.Directory = System.IO.Path.GetDirectoryName( this.Path )!;
        this.IsEvaluated = isEvaluated;
    }

    /// <summary>
    /// Gets the full path of the project file.
    /// </summary>
    public string Path { get; }

    /// <summary>
    /// Gets the name of the project: its file name without extension.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the directory of the project file.
    /// </summary>
    public string Directory { get; }

    /// <summary>
    /// Gets a value indicating whether the project was evaluated with MSBuild. When no compatible .NET SDK exists, the files are
    /// found by searching <c>bin</c> and <c>artifacts/bin</c> instead.
    /// </summary>
    public bool IsEvaluated { get; }

    /// <summary>
    /// Gets the build outputs of the target frameworks of the project, which have or should have a file.
    /// </summary>
    public IReadOnlyList<IntrospectionTarget> Targets => this.TargetsList;

    internal List<IntrospectionTarget> TargetsList { get; } = [];

    /// <inheritdoc />
    public override string ToString() => this.Name;
}

/// <summary>
/// The build output of one target framework and configuration of a project, with the file that describes how aspects were
/// applied to it.
/// </summary>
public sealed class IntrospectionTarget
{
    internal IntrospectionTarget(
        IntrospectionProject project,
        string targetFramework,
        string configuration,
        string assemblyName,
        string filePath,
        IReadOnlyDictionary<string, string> additionalFiles,
        DateTime? newestSourceWriteTimeUtc )
    {
        this.Project = project;
        this.TargetFramework = targetFramework;
        this.Configuration = configuration;
        this.AssemblyName = assemblyName;
        this.FilePath = filePath;
        this.AdditionalFiles = additionalFiles;

        var file = new FileInfo( filePath );

        if ( file.Exists )
        {
            this.WrittenAtUtc = file.LastWriteTimeUtc;
            this.IsStale = newestSourceWriteTimeUtc > file.LastWriteTimeUtc;
        }
    }

    /// <summary>
    /// Gets the project.
    /// </summary>
    public IntrospectionProject Project { get; }

    /// <summary>
    /// Gets the target framework, such as <c>net10.0</c>.
    /// </summary>
    public string TargetFramework { get; }

    /// <summary>
    /// Gets the configuration, such as <c>Debug</c>.
    /// </summary>
    public string Configuration { get; }

    /// <summary>
    /// Gets the name of the assembly that the project builds.
    /// </summary>
    public string AssemblyName { get; }

    /// <summary>
    /// Gets the full path of the file, which exists only when the project was built.
    /// </summary>
    public string FilePath { get; }

    /// <summary>
    /// Gets the other files of the product, by the property that gives their path, such as <c>PostSharpSymbolLocationFile</c>.
    /// </summary>
    public IReadOnlyDictionary<string, string> AdditionalFiles { get; }

    /// <summary>
    /// Gets a value indicating whether the file exists.
    /// </summary>
    public bool Exists => this.WrittenAtUtc != null;

    /// <summary>
    /// Gets the time at which the file was written, or <c>null</c> when it does not exist.
    /// </summary>
    public DateTime? WrittenAtUtc { get; }

    /// <summary>
    /// Gets a value indicating whether a source file or the project file is newer than the file, so that the file may not
    /// describe the current source.
    /// </summary>
    public bool IsStale { get; }

    /// <inheritdoc />
    public override string ToString() => $"{this.Project.Name} ({this.TargetFramework})";
}
