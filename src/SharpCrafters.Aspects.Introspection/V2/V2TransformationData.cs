// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

namespace SharpCrafters.Aspects.Introspection.V2;

/// <summary>
/// A transformation of the code, inside an aspect instance, in a file of schema version 2.
/// </summary>
public readonly struct V2TransformationData
{
    /// <summary>
    /// Gets the kind of the transformation (<c>kind</c>), for example <c>OverrideMember</c> or <c>IntroduceMember</c>.
    /// </summary>
    /// <remarks>
    /// The kind is the name of a member of the <c>IntrospectionTransformationKind</c> enumeration of Metalama. It is a string, so
    /// that a reader accepts a kind that a later version of the product adds.
    /// </remarks>
    public required string Kind { get; init; }

    /// <summary>
    /// Gets the kind of the advice that created the transformation (<c>adviceKind</c>), for example <c>OverrideMethod</c>, or
    /// <c>null</c>. It is the name of a member of the <c>AdviceKind</c> enumeration of Metalama.
    /// </summary>
    public string? AdviceKind { get; init; }

    /// <summary>
    /// Gets the name of the aspect layer of the advice (<c>layer</c>), or <c>null</c> for the default aspect layer.
    /// </summary>
    public string? Layer { get; init; }

    /// <summary>
    /// Gets the order of the transformation within the aspect instance (<c>order</c>). The implied value is <c>0</c>.
    /// </summary>
    public int Order { get; init; }

    /// <summary>
    /// Gets the declaration that the transformation changes (<c>targetDeclaration</c>).
    /// </summary>
    public required DeclarationHandle TargetDeclaration { get; init; }

    /// <summary>
    /// Gets the declaration that the transformation introduces (<c>introducedDeclaration</c>), such as an introduced member,
    /// parameter or interface, or a nil handle.
    /// </summary>
    public DeclarationHandle IntroducedDeclaration { get; init; }

    /// <summary>
    /// Gets a description of the transformation for a person (<c>description</c>), or <c>null</c>.
    /// </summary>
    public string? Description { get; init; }
}
