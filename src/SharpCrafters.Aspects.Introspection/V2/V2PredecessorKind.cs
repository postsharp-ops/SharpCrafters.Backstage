// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

namespace SharpCrafters.Aspects.Introspection.V2;

/// <summary>
/// The kind of a predecessor, in a file of schema version 2 (<c>kind</c>).
/// </summary>
public enum V2PredecessorKind
{
    /// <summary>
    /// A custom attribute. <see cref="V2PredecessorData.Declaration"/> is the declaration that carries it, and
    /// <see cref="V2PredecessorData.AttributeClass"/> is its type.
    /// </summary>
    Attribute,

    /// <summary>
    /// Another aspect instance, which created this aspect instance as a child aspect. <see cref="V2PredecessorData.AspectInstance"/>
    /// is the identifier of the parent aspect instance.
    /// </summary>
    ChildAspect,

    /// <summary>
    /// Another aspect instance, which requires this aspect instance. <see cref="V2PredecessorData.AspectInstance"/> is the
    /// identifier of that aspect instance.
    /// </summary>
    RequiredAspect,

    /// <summary>
    /// An aspect instance of a base declaration, from which this aspect instance is inherited.
    /// <see cref="V2PredecessorData.Declaration"/> is the base declaration.
    /// </summary>
    Inherited,

    /// <summary>
    /// A fabric. <see cref="V2PredecessorData.Declaration"/> is the type of the fabric.
    /// </summary>
    Fabric,

    /// <summary>
    /// A code action of the user interface.
    /// </summary>
    Interactive
}
