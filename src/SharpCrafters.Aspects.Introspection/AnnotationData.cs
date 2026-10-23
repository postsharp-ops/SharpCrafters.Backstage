// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

namespace SharpCrafters.Aspects.Introspection;

/// <summary>
/// An annotation of a declaration, such as a dependency between properties, the <c>Annotation</c> element of version 1.
/// </summary>
public readonly struct AnnotationData
{
    /// <summary>
    /// Gets the annotated declaration (<c>Target</c>).
    /// </summary>
    public required DeclarationHandle TargetDeclaration { get; init; }

    /// <summary>
    /// Gets the class of the annotation (<c>Class</c>), or a nil handle.
    /// </summary>
    public DeclarationHandle AnnotationClass { get; init; }

    /// <summary>
    /// Gets the arguments of the annotation (<c>Arguments</c>), or <c>null</c>.
    /// </summary>
    public string? Arguments { get; init; }

    /// <summary>
    /// Gets a description of the annotation (<c>Description</c>), or <c>null</c>.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Gets the number of lines of code that the annotation stands for (<c>LinesAvoided</c>, implied <c>0</c>).
    /// </summary>
    public int LinesOfCodeAvoided { get; init; }
}
