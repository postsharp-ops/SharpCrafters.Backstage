// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

namespace SharpCrafters.Aspects.Introspection;

/// <summary>
/// The kind of the node on which a reader is positioned (<see cref="IntrospectionV1Reader.NodeKind"/>).
/// </summary>
public enum IntrospectionNodeKind
{
    /// <summary>
    /// The reader is not positioned on a node: <see cref="IntrospectionV1Reader.Read"/> was not called, or it returned <c>false</c>.
    /// </summary>
    None,

    /// <summary>
    /// A declaration mapping (<see cref="IntrospectionV1Reader.DeclarationMapping"/>).
    /// </summary>
    DeclarationMapping,

    /// <summary>
    /// The start of an aspect class (<see cref="IntrospectionV1Reader.AspectClass"/>).
    /// </summary>
    AspectClass,

    /// <summary>
    /// The end of an aspect class.
    /// </summary>
    EndAspectClass,

    /// <summary>
    /// The start of a predecessor (<see cref="IntrospectionV1Reader.Predecessor"/>).
    /// </summary>
    Predecessor,

    /// <summary>
    /// The end of a predecessor.
    /// </summary>
    EndPredecessor,

    /// <summary>
    /// The start of an aspect instance (<see cref="IntrospectionV1Reader.AspectInstance"/>).
    /// </summary>
    AspectInstance,

    /// <summary>
    /// The end of an aspect instance.
    /// </summary>
    EndAspectInstance,

    /// <summary>
    /// A transformation (<see cref="IntrospectionV1Reader.Transformation"/>).
    /// </summary>
    Transformation,

    /// <summary>
    /// An annotation (<see cref="IntrospectionV1Reader.Annotation"/>).
    /// </summary>
    Annotation
}
