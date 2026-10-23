// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

namespace SharpCrafters.Aspects.Introspection;

/// <summary>
/// An aspect instance, the <c>Target</c> element of version 1. It contains its transformations.
/// </summary>
public readonly struct AspectInstanceData
{
    /// <summary>
    /// Gets the declaration to which the aspect instance is applied (<c>Target</c>). The format implies the
    /// <see cref="PredecessorData.Declaration"/> of the enclosing predecessor: a writer omits the attribute when they are equal, and
    /// a reader returns the implied value when the attribute is absent.
    /// </summary>
    public required DeclarationHandle TargetDeclaration { get; init; }

    /// <summary>
    /// Gets the number of lines of code that the aspect instance saves (<c>LinesAvoided</c>, implied <c>0</c>).
    /// </summary>
    public int LinesOfCodeAvoided { get; init; }

    /// <summary>
    /// Gets the tags of the aspect instance (<c>Tags</c>), such as <c>premium</c> for an aspect instance that the licence does not
    /// cover, or <c>null</c>.
    /// </summary>
    public string? Tags { get; init; }

    /// <summary>
    /// Gets the identifier of the aspect instance (<c>Id</c>), unique in the file, or <c>null</c>.
    /// </summary>
    public string? Id { get; init; }
}
