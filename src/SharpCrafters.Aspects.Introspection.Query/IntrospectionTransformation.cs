// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

namespace SharpCrafters.Aspects.Introspection.Query;

/// <summary>
/// One change that an advice of an aspect instance makes to a declaration.
/// </summary>
public sealed class IntrospectionTransformation
{
    internal IntrospectionTransformation( IntrospectionAspectInstance aspectInstance, IntrospectionDeclaration targetDeclaration )
    {
        this.AspectInstance = aspectInstance;
        this.TargetDeclaration = targetDeclaration;
    }

    /// <summary>
    /// Gets the aspect instance that makes the transformation.
    /// </summary>
    public IntrospectionAspectInstance AspectInstance { get; }

    /// <summary>
    /// Gets the declaration that the transformation changes.
    /// </summary>
    public IntrospectionDeclaration TargetDeclaration { get; }

    /// <summary>
    /// Gets the declaration of the advice that makes the transformation (PostSharp), or <c>null</c>.
    /// </summary>
    public IntrospectionDeclaration? AdviceDeclaration { get; internal set; }

    /// <summary>
    /// Gets a description of the transformation, or <c>null</c>.
    /// </summary>
    public string? Description { get; internal set; }

    /// <summary>
    /// Gets the role of the accessor that the transformation changes, such as <c>Getter</c>, or <c>null</c>.
    /// </summary>
    public string? Semantic { get; internal set; }

    /// <summary>
    /// Gets the order of the transformation. In a file of version 1, it is a counter of the whole assembly.
    /// </summary>
    public int Order { get; internal set; }

    /// <summary>
    /// Gets the number of lines of code that the transformation saves, or <c>null</c>.
    /// </summary>
    public int? LinesOfCodeAvoided { get; internal set; }

    /// <inheritdoc />
    public override string ToString() => $"{this.AspectInstance} -> {this.TargetDeclaration}";
}
