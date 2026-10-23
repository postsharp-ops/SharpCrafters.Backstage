// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

namespace SharpCrafters.Aspects.Introspection;

/// <summary>
/// An aspect class, the <c>Class</c> element of version 1. It contains the predecessors of its aspect instances.
/// </summary>
public readonly struct AspectClassData
{
    /// <summary>
    /// Gets the aspect class (<c>Class</c>).
    /// </summary>
    public required DeclarationHandle AspectClass { get; init; }

    /// <summary>
    /// Gets a value indicating whether the licence covers the aspect class only in part (<c>LimitedLicense</c>).
    /// </summary>
    public bool HasLimitedLicense { get; init; }
}
