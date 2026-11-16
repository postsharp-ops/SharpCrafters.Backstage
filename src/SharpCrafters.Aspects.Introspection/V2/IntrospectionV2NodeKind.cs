// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

namespace SharpCrafters.Aspects.Introspection.V2;

/// <summary>
/// The kind of the node on which an <see cref="IntrospectionV2Reader"/> is positioned (<see cref="IntrospectionV2Reader.NodeKind"/>).
/// </summary>
public enum IntrospectionV2NodeKind
{
    /// <summary>
    /// The reader is not positioned on a node: <see cref="IntrospectionV2Reader.Read"/> was not called, or it returned <c>false</c>.
    /// </summary>
    None,

    /// <summary>
    /// The start of an aspect class (<see cref="IntrospectionV2Reader.AspectClass"/>).
    /// </summary>
    AspectClass,

    /// <summary>
    /// The end of an aspect class.
    /// </summary>
    EndAspectClass,

    /// <summary>
    /// An aspect layer of the current aspect class (<see cref="IntrospectionV2Reader.AspectLayer"/>).
    /// </summary>
    AspectLayer,

    /// <summary>
    /// The start of an aspect instance (<see cref="IntrospectionV2Reader.AspectInstance"/>).
    /// </summary>
    AspectInstance,

    /// <summary>
    /// The end of an aspect instance.
    /// </summary>
    EndAspectInstance,

    /// <summary>
    /// A predecessor of the current aspect instance (<see cref="IntrospectionV2Reader.Predecessor"/>).
    /// </summary>
    Predecessor,

    /// <summary>
    /// A transformation of the current aspect instance (<see cref="IntrospectionV2Reader.Transformation"/>).
    /// </summary>
    Transformation,

    /// <summary>
    /// A diagnostic of the current aspect instance (<see cref="IntrospectionV2Reader.Diagnostic"/>).
    /// </summary>
    Diagnostic
}
