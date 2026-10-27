// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

namespace SharpCrafters.Aspects.Introspection;

/// <summary>
/// The root of a weaving symbol file, the <c>Symbols</c> element of version 1.
/// </summary>
public readonly struct SymbolsData
{
    /// <summary>
    /// Gets the version of the product that wrote the file (<c>PostSharpVersion</c>), or <c>null</c>.
    /// </summary>
    public string? ProductVersion { get; init; }

    /// <summary>
    /// Gets the number of lines of code of the assembly (<c>LinesOfCode</c>).
    /// </summary>
    public int LinesOfCode { get; init; }

    /// <summary>
    /// Gets the number of lines of code of the assembly that the product counts as its own (<c>PostSharpLinesOfCode</c>).
    /// </summary>
    public int ProductLinesOfCode { get; init; }

    /// <summary>
    /// Gets the number of types that an aspect enhances (<c>EnhancedTypesCount</c>).
    /// </summary>
    public int EnhancedTypesCount { get; init; }
}
