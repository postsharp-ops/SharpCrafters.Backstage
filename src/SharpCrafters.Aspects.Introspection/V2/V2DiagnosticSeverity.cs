// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

namespace SharpCrafters.Aspects.Introspection.V2;

/// <summary>
/// The severity of a diagnostic, in a file of schema version 2 (<c>severity</c>).
/// </summary>
public enum V2DiagnosticSeverity
{
    /// <summary>
    /// A diagnostic that the user interface does not show.
    /// </summary>
    Hidden,

    /// <summary>
    /// An information message.
    /// </summary>
    Info,

    /// <summary>
    /// A warning.
    /// </summary>
    Warning,

    /// <summary>
    /// An error.
    /// </summary>
    Error
}
