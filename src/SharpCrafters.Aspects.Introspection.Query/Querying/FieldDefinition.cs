// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using System;
using System.Collections.Generic;

namespace SharpCrafters.Aspects.Introspection.Query.Querying;

/// <summary>
/// A field of a type of the schema: its name in the JSON output and in the conditions, its kind, and how its value is read.
/// </summary>
internal sealed class FieldDefinition
{
    public FieldDefinition( string name, ValueKind kind, string description, Func<QueryContext, object, object?> getValue )
    {
        this.Name = name;
        this.Kind = kind;
        this.Description = description;
        this.GetValue = getValue;
    }

    public string Name { get; }

    public ValueKind Kind { get; }

    public string Description { get; }

    public Func<QueryContext, object, object?> GetValue { get; }

    /// <summary>
    /// Gets the type of an <see cref="ValueKind.Object"/> or <see cref="ValueKind.ObjectList"/> field.
    /// </summary>
    public string? TypeName { get; init; }

    /// <summary>
    /// Gets the values of an <see cref="ValueKind.Enum"/> field.
    /// </summary>
    public IReadOnlyList<string>? EnumValues { get; init; }

    /// <summary>
    /// Gets a value indicating whether the field is written when an object of the type is written inside another one.
    /// </summary>
    public bool IsInSummary { get; init; }

    /// <summary>
    /// Gets a value indicating whether the field is written by default. A field that is not, such as the structural key, which
    /// the identifier summarizes, is written only when <c>--select</c> names it.
    /// </summary>
    public bool IsWrittenByDefault { get; init; } = true;
}
