// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

namespace SharpCrafters.Aspects.Introspection;

/// <summary>
/// A transformation of a declaration by an aspect instance, the <c>JoinPoint</c> element of version 1.
/// </summary>
public readonly struct TransformationData
{
    /// <summary>
    /// Gets the declaration that the transformation changes (<c>Advised</c>). The format implies the
    /// <see cref="AspectInstanceData.TargetDeclaration"/> of the enclosing aspect instance: a writer omits the attribute when they
    /// are equal or when the handle is nil, and a reader returns the implied value when the attribute is absent.
    /// </summary>
    public DeclarationHandle TargetDeclaration { get; init; }

    /// <summary>
    /// Gets the advice that does the transformation (<c>Advising</c>). The format implies the
    /// <see cref="AspectClassData.AspectClass"/> of the enclosing aspect class: a writer omits the attribute when they are equal or
    /// when the handle is nil, and a reader returns the implied value when the attribute is absent.
    /// </summary>
    public DeclarationHandle AdviceDeclaration { get; init; }

    /// <summary>
    /// Gets a description of the transformation (<c>Description</c>), or <c>null</c>.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Gets the role of the accessor that the transformation changes (<c>Semantic</c>), such as <c>Getter</c>, or <c>null</c>.
    /// </summary>
    public string? Semantic { get; init; }

    /// <summary>
    /// Gets the order of the transformation (<c>Ordinal</c>, implied <c>1</c>).
    /// </summary>
    public required int Order { get; init; }

    /// <summary>
    /// Gets the number of lines of code that the transformation saves (<c>LinesAvoided</c>), or <c>null</c>.
    /// </summary>
    public int? LinesOfCodeAvoided { get; init; }
}
