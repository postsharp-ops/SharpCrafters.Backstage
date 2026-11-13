// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

namespace SharpCrafters.Aspects.Introspection;

/// <summary>
/// The properties of the root object of a file of schema version 2, the <c>.mlsym</c> file of Metalama.
/// </summary>
public readonly struct V2HeaderData
{
    /// <summary>
    /// Gets the name of the product that wrote the file (<c>product</c>), for example <c>Metalama</c>.
    /// </summary>
    public required string Product { get; init; }

    /// <summary>
    /// Gets the version of the product that wrote the file (<c>productVersion</c>), or <c>null</c>.
    /// </summary>
    public string? ProductVersion { get; init; }

    /// <summary>
    /// Gets the name of the assembly that the compilation produces (<c>assembly</c>), or <c>null</c>.
    /// </summary>
    public string? Assembly { get; init; }

    /// <summary>
    /// Gets the target framework of the compilation (<c>targetFramework</c>), for example <c>net10.0</c>, or <c>null</c>.
    /// </summary>
    public string? TargetFramework { get; init; }

    /// <summary>
    /// Gets a value indicating whether the compilation succeeded (<c>compilationSucceeded</c>). The file of a compilation that
    /// failed can be incomplete. The writer always writes this property.
    /// </summary>
    public bool CompilationSucceeded { get; init; }
}
