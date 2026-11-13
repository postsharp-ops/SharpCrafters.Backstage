// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

namespace SharpCrafters.Aspects.Introspection.Query;

/// <summary>
/// A diagnostic that an aspect instance reported (Metalama).
/// </summary>
public sealed class IntrospectionDiagnostic
{
    internal IntrospectionDiagnostic( IntrospectionAspectInstance aspectInstance, string id, string severity )
    {
        this.AspectInstance = aspectInstance;
        this.Id = id;
        this.Severity = severity;
    }

    /// <summary>
    /// Gets the aspect instance that reported the diagnostic.
    /// </summary>
    public IntrospectionAspectInstance AspectInstance { get; }

    /// <summary>
    /// Gets the identifier of the diagnostic, such as <c>LAMA0041</c>.
    /// </summary>
    public string Id { get; }

    /// <summary>
    /// Gets the severity of the diagnostic: <c>Hidden</c>, <c>Info</c>, <c>Warning</c> or <c>Error</c>.
    /// </summary>
    public string Severity { get; }

    /// <summary>
    /// Gets the message of the diagnostic, or <c>null</c>.
    /// </summary>
    public string? Message { get; internal set; }

    /// <summary>
    /// Gets the declaration on which the diagnostic is reported, or <c>null</c>.
    /// </summary>
    public IntrospectionDeclaration? Declaration { get; internal set; }

    /// <summary>
    /// Gets the path of the file of the diagnostic, as the file gives it, or <c>null</c>.
    /// </summary>
    public string? File { get; internal set; }

    /// <summary>
    /// Gets the line of the diagnostic in <see cref="File"/>, or <c>null</c>.
    /// </summary>
    public int? Line { get; internal set; }

    /// <summary>
    /// Gets the column of the diagnostic in <see cref="File"/>, or <c>null</c>.
    /// </summary>
    public int? Column { get; internal set; }

    /// <inheritdoc />
    public override string ToString() => $"{this.Severity} {this.Id}: {this.Message}";
}
