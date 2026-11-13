// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

namespace SharpCrafters.Aspects.Introspection.V2;

/// <summary>
/// A position in a source file (<c>location</c>), in a file of schema version 2.
/// </summary>
public readonly struct V2SourceLocation
{
    /// <summary>
    /// Gets the path of the source file (<c>file</c>).
    /// </summary>
    public required string File { get; init; }

    /// <summary>
    /// Gets the line in <see cref="File"/>, starting at <c>1</c> (<c>line</c>), or <c>null</c>.
    /// </summary>
    public int? Line { get; init; }

    /// <summary>
    /// Gets the column in <see cref="Line"/>, starting at <c>1</c> (<c>column</c>), or <c>null</c>. A writer writes it only with
    /// <see cref="Line"/>.
    /// </summary>
    public int? Column { get; init; }

    /// <summary>
    /// Gets the last line of the declaration that starts at this location (<c>endLine</c>), or <c>null</c>. A reader uses it to find
    /// the declaration that contains a line. A writer writes it only with <see cref="Line"/>.
    /// </summary>
    public int? EndLine { get; init; }
}
