// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

namespace SharpCrafters.Aspects.Introspection.V2;

/// <summary>
/// A predecessor of an aspect instance, which is what created the aspect instance. In a file of schema version 2, a predecessor
/// is a child of the aspect instance that it created, and an aspect instance can have several predecessors.
/// </summary>
public readonly struct V2PredecessorData
{
    /// <summary>
    /// Gets the kind of the predecessor (<c>kind</c>).
    /// </summary>
    public required V2PredecessorKind Kind { get; init; }

    /// <summary>
    /// Gets the number of aspect instances between the predecessor and the source code (<c>degree</c>). It is <c>0</c> for a
    /// predecessor of the source code, such as a custom attribute or a fabric. The implied value is <c>0</c>.
    /// </summary>
    public int Degree { get; init; }

    /// <summary>
    /// Gets the declaration of the predecessor (<c>declaration</c>), or a nil handle. Its meaning depends on <see cref="Kind"/>.
    /// </summary>
    public DeclarationHandle Declaration { get; init; }

    /// <summary>
    /// Gets the type of the custom attribute (<c>attributeClass</c>) when <see cref="Kind"/> is
    /// <see cref="V2PredecessorKind.Attribute"/>, or a nil handle.
    /// </summary>
    public DeclarationHandle AttributeClass { get; init; }

    /// <summary>
    /// Gets the identifier of the aspect instance that is the predecessor (<c>aspectInstance</c>) when <see cref="Kind"/> is
    /// <see cref="V2PredecessorKind.ChildAspect"/> or <see cref="V2PredecessorKind.RequiredAspect"/>, or <c>null</c>. It is the
    /// <see cref="V2AspectInstanceData.Id"/> of an aspect instance of the same file.
    /// </summary>
    public int? AspectInstance { get; init; }

    /// <summary>
    /// Gets the source location of the predecessor (<c>location</c>), such as the position of the custom attribute, or <c>null</c>.
    /// </summary>
    public V2SourceLocation? Location { get; init; }
}
