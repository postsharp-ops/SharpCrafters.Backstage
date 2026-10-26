// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace SharpCrafters.Aspects.Introspection.Query.Querying;

/// <summary>
/// A path of fields, from the current object or from the variable of a lambda, optionally ending with <c>$count</c>.
/// </summary>
internal sealed class PathExpression( string? variable, IReadOnlyList<FieldDefinition> fields, bool isCount, ExpressionType type, string text ) : QueryExpression( type )
{
    public string? Variable { get; } = variable;

    public IReadOnlyList<FieldDefinition> Fields { get; } = fields;

    public bool IsCount { get; } = isCount;

    public string Text { get; } = text;

    public override object? Evaluate( QueryContext context, object current, IReadOnlyDictionary<string, object?> variables )
    {
        var value = this.Variable == null ? current : variables[this.Variable];

        foreach ( var field in this.Fields )
        {
            if ( value == null )
            {
                return this.IsCount ? 0L : null;
            }

            value = Normalize( field.GetValue( context, value ) );
        }

        return this.IsCount ? value is IEnumerable list && value is not string ? list.Cast<object>().Count() : 0L : value;
    }

    // Integers are compared as long, and empty strings are null, as an absent attribute of the file.
    private static object? Normalize( object? value )
        => value switch
        {
            int i => (long) i,
            string { Length: 0 } => null,
            _ => value
        };
}
