// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using System;
using System.Collections.Generic;

namespace SharpCrafters.Aspects.Introspection.Query.Querying;

/// <summary>
/// An expression of the subset of the OData <c>$filter</c> syntax that <c>--where</c> takes, bound to the schema
/// (Documentation/future/Introspection-Cli.md of PostSharp, section 5.4). Its <see cref="Evaluate"/> returns a string, a
/// <see cref="long"/>, a <see cref="bool"/>, an object of the model, a list, or <c>null</c>.
/// </summary>
internal abstract class QueryExpression
{
    protected QueryExpression( ExpressionType type )
    {
        this.Type = type;
    }

    public ExpressionType Type { get; }

    public abstract object? Evaluate( QueryContext context, object current, IReadOnlyDictionary<string, object?> variables );

    public bool IsTrue( QueryContext context, object current )
        => this.Evaluate( context, current, new Dictionary<string, object?>( StringComparer.Ordinal ) ) is true;
}
