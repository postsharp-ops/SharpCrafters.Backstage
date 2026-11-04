// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using System.Collections.Generic;

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
    /// found by searching the <c>bin</c> directory of the project and the <c>artifacts/bin</c> directory of the repository instead.
    /// </summary>
    public bool IsEvaluated { get; }

    /// <summary>
    /// Gets the build outputs of the target frameworks of the project, which have or should have a file.
    /// </summary>
    public IReadOnlyList<IntrospectionTarget> Targets => this.TargetsList;

    internal List<IntrospectionTarget> TargetsList { get; } = [];

    /// <summary>
    /// Gets the target frameworks of the project, including those that <c>-f</c> leaves out of the scope, or an empty list when the
    /// project was not evaluated.
    /// </summary>
    public IReadOnlyList<string> TargetFrameworks => this.TargetFrameworksList;

    internal List<string> TargetFrameworksList { get; } = [];

    /// <inheritdoc />
    public override string ToString() => this.Name;
}
