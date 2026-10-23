// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

namespace SharpCrafters.Aspects.Introspection;

/// <summary>
/// A predecessor: what creates aspect instances, typically a custom attribute, the <c>Instance</c> element of version 1. It
/// contains the aspect instances that it creates.
/// </summary>
public readonly struct PredecessorData
{
    /// <summary>
    /// Gets the declaration that carries the predecessor (<c>Declaration</c>), or a nil handle, as for an XML element.
    /// </summary>
    public DeclarationHandle Declaration { get; init; }

    /// <summary>
    /// Gets the path of the file that declares the predecessor (<c>File</c>), or <c>null</c>.
    /// </summary>
    public string? File { get; init; }

    /// <summary>
    /// Gets the line of the predecessor in <see cref="File"/> (<c>Line</c>). A writer writes it only with <see cref="File"/>.
    /// </summary>
    public int? Line { get; init; }

    /// <summary>
    /// Gets the column of the predecessor in <see cref="File"/> (<c>Column</c>). A writer writes it only with <see cref="Line"/>.
    /// </summary>
    public int? Column { get; init; }

    /// <summary>
    /// Gets a value indicating whether the predecessor is a custom attribute of another assembly (<c>ForwardRef</c>).
    /// </summary>
    public bool IsForwardReference { get; init; }

    /// <summary>
    /// Gets a description of the predecessor (<c>Description</c>), or <c>null</c>.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Gets the identifier of the predecessor (<c>Id</c>), or <c>null</c>.
    /// </summary>
    public string? Id { get; init; }

    /// <summary>
    /// Gets how the predecessor creates its aspect instances (<c>Origin</c>), or <c>null</c> when the file does not give it.
    /// </summary>
    public PredecessorKind? Kind { get; init; }

    /// <summary>
    /// Gets the identifier of the aspect instance whose aspect provider created the aspect instances of this predecessor
    /// (<c>ProvidedBy</c>), when <see cref="Kind"/> is <see cref="PredecessorKind.ChildAspect"/>, or <c>null</c>.
    /// </summary>
    public string? ProvidedBy { get; init; }
}
