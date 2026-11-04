// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace SharpCrafters.Aspects.Introspection.Query.Querying;

/// <summary>
/// A path of <c>--select</c>: fields from the current object, optionally ending with <c>$count</c> after a collection.
/// </summary>
internal sealed class FieldPath
{
    public FieldPath( IReadOnlyList<FieldDefinition> fields, bool isCount, string text )
    {
        this.Fields = fields;
        this.IsCount = isCount;
        this.Text = text;
    }

    public IReadOnlyList<FieldDefinition> Fields { get; }

    public bool IsCount { get; }

    // The path as written, such as transformations/$count.
    public string Text { get; }

    public object? Evaluate( QueryContext context, object current )
    {
        var value = (object?) current;

        foreach ( var field in this.Fields )
        {
            if ( value == null )
            {
                return this.IsCount ? 0L : null;
            }

            value = QueryValues.Normalize( field.GetValue( context, value ) );
        }

        return this.IsCount ? value is IEnumerable list and not string ? list.Cast<object>().Count() : 0L : value;
    }
}
