// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

namespace SharpCrafters.Aspects.Introspection.Query;

/// <summary>
/// Information about a declaration that is not a transformation, such as a dependency between properties (PostSharp).
/// </summary>
public sealed class IntrospectionAnnotation
{
    internal IntrospectionAnnotation( IntrospectionDeclaration targetDeclaration )
    {
        this.TargetDeclaration = targetDeclaration;
    }

    /// <summary>
    /// Gets the annotated declaration.
    /// </summary>
    public IntrospectionDeclaration TargetDeclaration { get; }

    /// <summary>
    /// Gets the class of the annotation, or <c>null</c>.
    /// </summary>
    public IntrospectionDeclaration? AnnotationClass { get; internal set; }

    /// <summary>
    /// Gets the arguments of the annotation, or <c>null</c>.
    /// </summary>
    public string? Arguments { get; internal set; }

    /// <summary>
    /// Gets a description of the annotation, or <c>null</c>.
    /// </summary>
    public string? Description { get; internal set; }

    /// <summary>
    /// Gets the number of lines of code that the annotation stands for.
    /// </summary>
    public int LinesOfCodeAvoided { get; internal set; }
}
