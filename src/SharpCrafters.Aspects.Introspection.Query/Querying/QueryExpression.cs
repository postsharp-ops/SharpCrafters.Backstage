// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using System;

namespace SharpCrafters.Aspects.Introspection.Query.Querying;

/// <summary>
/// An expression of <c>--where</c> or <c>--order-by</c>, bound by Microsoft.OData.Core and compiled by
/// <see cref="QueryCompiler"/>. Its <see cref="Evaluate"/> returns a string, a <see cref="long"/>, a <see cref="double"/>, a
/// <see cref="bool"/>, an object of the model, a list, or <c>null</c>.
/// </summary>
internal sealed class QueryExpression
{
    private readonly Func<QueryScope, object?> _evaluate;

    public QueryExpression( Func<QueryScope, object?> evaluate )
    {
        this._evaluate = evaluate;
    }

    public object? Evaluate( QueryContext context, object current ) => this._evaluate( new QueryScope( context, current ) );

    public bool IsTrue( QueryContext context, object current ) => this.Evaluate( context, current ) is true;
}
