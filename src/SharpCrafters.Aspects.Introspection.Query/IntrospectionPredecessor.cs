// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using System.Collections.Generic;

namespace SharpCrafters.Aspects.Introspection.Query;

/// <summary>
/// What creates aspect instances: a custom attribute, a multicast attribute, an XML element, a fabric, or another aspect
/// instance.
/// </summary>
public sealed class IntrospectionPredecessor
{
    internal IntrospectionPredecessor( AspectPredecessorKind kind, bool isKindInferred )
    {
        this.Kind = kind;
        this.IsKindInferred = isKindInferred;
    }

    /// <summary>
    /// Gets the identifier of the predecessor: the first 48 bits of the hash of <see cref="StructuralKey"/>, as 12 lowercase
    /// hexadecimal characters. It stays the same across builds as long as the source does not change.
    /// </summary>
    public string Id { get; internal set; } = "";

    /// <summary>
    /// Gets the key from which <see cref="Id"/> is computed, such as <c>attribute:[App]T:App.Customer:App.LogAttribute</c>.
    /// </summary>
    public string StructuralKey { get; internal set; } = "";

    /// <summary>
    /// Gets how the predecessor creates aspect instances.
    /// </summary>
    public AspectPredecessorKind Kind { get; }

    /// <summary>
    /// Gets a value indicating whether <see cref="Kind"/> is inferred, because the file does not give it.
    /// </summary>
    public bool IsKindInferred { get; }

    /// <summary>
    /// Gets the declaration that carries the custom attribute, for a predecessor of kind <see cref="AspectPredecessorKind.Attribute"/>,
    /// <see cref="AspectPredecessorKind.Multicast"/> or <see cref="AspectPredecessorKind.Inherited"/>, or the fabric type, or <c>null</c>.
    /// </summary>
    public IntrospectionDeclaration? Declaration { get; internal set; }

    /// <summary>
    /// Gets the class of the custom attribute, or <c>null</c>.
    /// </summary>
    public IntrospectionAspectClass? AttributeClass { get; internal set; }

    /// <summary>
    /// Gets the path of the file that declares the predecessor, as the file gives it, or <c>null</c>.
    /// </summary>
    public string? File { get; internal set; }

    // The path of File in the structural key: relative to the directory of the project when the loader knows it.
    internal string? KeyFile { get; set; }

    /// <summary>
    /// Gets the line of the predecessor in <see cref="File"/>, or <c>null</c>.
    /// </summary>
    public int? Line { get; internal set; }

    /// <summary>
    /// Gets the column of the predecessor in <see cref="File"/>, or <c>null</c>.
    /// </summary>
    public int? Column { get; internal set; }

    /// <summary>
    /// Gets the aspect instance that provides the aspect instances of this predecessor, for a predecessor of kind
    /// <see cref="AspectPredecessorKind.ChildAspect"/>, or <c>null</c>.
    /// </summary>
    public IntrospectionAspectInstance? AspectInstance { get; internal set; }

    /// <summary>
    /// Gets the aspect instances that the predecessor creates.
    /// </summary>
    public IReadOnlyList<IntrospectionAspectInstance> AspectInstances => this.AspectInstancesList;

    internal List<IntrospectionAspectInstance> AspectInstancesList { get; } = [];

    /// <inheritdoc />
    public override string ToString() => this.StructuralKey;
}
