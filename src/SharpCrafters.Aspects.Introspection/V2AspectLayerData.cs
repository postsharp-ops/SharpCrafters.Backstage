// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

namespace SharpCrafters.Aspects.Introspection;

/// <summary>
/// An aspect layer of an aspect class, in a file of schema version 2.
/// </summary>
public readonly struct V2AspectLayerData
{
    /// <summary>
    /// Gets the name of the aspect layer (<c>name</c>), or <c>null</c> for the default aspect layer.
    /// </summary>
    public string? Name { get; init; }

    /// <summary>
    /// Gets the order of execution of the aspect layer among all the aspect layers of the compilation (<c>order</c>). The implied
    /// value is <c>0</c>.
    /// </summary>
    public int Order { get; init; }
}
