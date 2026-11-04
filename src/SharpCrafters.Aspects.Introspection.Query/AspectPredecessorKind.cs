// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

namespace SharpCrafters.Aspects.Introspection.Query;

/// <summary>
/// How a predecessor creates aspect instances (<see cref="IntrospectionPredecessor.Kind"/>): the union of the kinds of PostSharp
/// and Metalama.
/// </summary>
public enum AspectPredecessorKind
{
    /// <summary>
    /// A custom attribute on the target declaration.
    /// </summary>
    Attribute,

    /// <summary>
    /// A custom attribute on an enclosing declaration, multicast to the target declarations (PostSharp).
    /// </summary>
    Multicast,

    /// <summary>
    /// A custom attribute inherited from a base declaration.
    /// </summary>
    Inherited,

    /// <summary>
    /// An element of an XML configuration file (PostSharp).
    /// </summary>
    Xml,

    /// <summary>
    /// A fabric (Metalama).
    /// </summary>
    Fabric,

    /// <summary>
    /// An aspect instance that provides child aspect instances: an aspect provider of PostSharp, or an aspect of Metalama that
    /// adds aspects.
    /// </summary>
    ChildAspect,

    /// <summary>
    /// An aspect instance that requires another aspect (Metalama).
    /// </summary>
    RequiredAspect,

    /// <summary>
    /// An interactive action of the user in the integrated development environment (Metalama).
    /// </summary>
    Interactive
}
