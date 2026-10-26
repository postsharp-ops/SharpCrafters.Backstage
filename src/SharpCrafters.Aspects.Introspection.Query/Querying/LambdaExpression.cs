// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace SharpCrafters.Aspects.Introspection.Query.Querying;

/// <summary>
/// <c>path/any(x: condition)</c>, <c>path/all(x: condition)</c>, or <c>path/any()</c> for a non-empty collection.
/// </summary>
internal sealed class LambdaExpression( QueryExpression collection, bool isAll, string? variable, QueryExpression? body ) : QueryExpression( ExpressionType.Boolean )
{
    public override object? Evaluate( QueryContext context, object current, IReadOnlyDictionary<string, object?> variables )
    {
        if ( collection.Evaluate( context, current, variables ) is not IEnumerable items )
        {
            return isAll;
        }

        if ( body == null )
        {
            return items.Cast<object>().Any();
        }

        var scope = new Dictionary<string, object?>( variables, StringComparer.Ordinal );

        bool Test( object item )
        {
            scope[variable!] = item;

            return body.Evaluate( context, current, scope ) is true;
        }

        return isAll ? items.Cast<object>().All( Test ) : items.Cast<object>().Any( Test );
    }
}
