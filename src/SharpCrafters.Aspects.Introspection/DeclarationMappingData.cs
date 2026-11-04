// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

namespace SharpCrafters.Aspects.Introspection;

/// <summary>
/// A declaration mapping: the declaration of the source code that a declaration of the woven assembly stands for, the
/// <c>Evolution</c> element of version 1.
/// </summary>
public readonly struct DeclarationMappingData
{
    /// <summary>
    /// Gets the source declaration (<c>Source</c>).
    /// </summary>
    public required DeclarationHandle Source { get; init; }

    /// <summary>
    /// Gets the target declaration (<c>Target</c>).
    /// </summary>
    public required DeclarationHandle Target { get; init; }

    /// <summary>
    /// Gets the kind of the mapping (<c>Kind</c>), or <c>null</c> when the file does not give it.
    /// </summary>
    public DeclarationMappingKind? Kind { get; init; }
}
