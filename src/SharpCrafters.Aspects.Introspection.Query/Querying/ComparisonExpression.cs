// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using System.Collections.Generic;

namespace SharpCrafters.Aspects.Introspection.Query.Querying;

internal sealed class ComparisonExpression( QueryExpression left, string op, QueryExpression right ) : QueryExpression( ExpressionType.Boolean )
{
    public override object? Evaluate( QueryContext context, object current, IReadOnlyDictionary<string, object?> variables )
    {
        var l = left.Evaluate( context, current, variables );
        var r = right.Evaluate( context, current, variables );

        switch ( op )
        {
            case "eq":
                return Equals( l, r );

            case "ne":
                return !Equals( l, r );
        }

        // An ordering with null is false, as in OData.
        if ( l == null || r == null )
        {
            return false;
        }

        var comparison = l is string ls ? string.CompareOrdinal( ls, (string) r ) : Comparer<object>.Default.Compare( l, r );

        return op switch
        {
            "gt" => comparison > 0,
            "ge" => comparison >= 0,
            "lt" => comparison < 0,
            _ => comparison <= 0
        };
    }
}
