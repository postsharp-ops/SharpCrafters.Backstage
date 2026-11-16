// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

namespace SharpCrafters.Aspects.Introspection.V2;

/// <summary>
/// A diagnostic that an aspect instance reported, inside the aspect instance, in a file of schema version 2.
/// </summary>
public readonly struct V2DiagnosticData
{
    /// <summary>
    /// Gets the identifier of the diagnostic (<c>id</c>), for example <c>LAMA0041</c>.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Gets the severity of the diagnostic (<c>severity</c>).
    /// </summary>
    public required V2DiagnosticSeverity Severity { get; init; }

    /// <summary>
    /// Gets the message of the diagnostic (<c>message</c>), or <c>null</c>.
    /// </summary>
    public string? Message { get; init; }

    /// <summary>
    /// Gets the declaration on which the diagnostic is reported (<c>declaration</c>), or a nil handle.
    /// </summary>
    public DeclarationHandle Declaration { get; init; }

    /// <summary>
    /// Gets the source location of the diagnostic (<c>location</c>), or <c>null</c>.
    /// </summary>
    public V2SourceLocation? Location { get; init; }
}
