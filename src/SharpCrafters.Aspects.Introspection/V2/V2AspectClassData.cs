// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

namespace SharpCrafters.Aspects.Introspection.V2;

/// <summary>
/// The start of an aspect class, in a file of schema version 2. It contains the aspect layers, then the aspect instances.
/// </summary>
public readonly struct V2AspectClassData
{
    /// <summary>
    /// Gets the type of the aspect class (<c>id</c>).
    /// </summary>
    public required DeclarationHandle AspectClass { get; init; }

    /// <summary>
    /// Gets the name of the aspect class as the user interface shows it (<c>displayName</c>), or <c>null</c>.
    /// </summary>
    public string? DisplayName { get; init; }

    /// <summary>
    /// Gets the description of the aspect class (<c>description</c>), or <c>null</c>.
    /// </summary>
    public string? Description { get; init; }
}
