// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

namespace SharpCrafters.Aspects.Introspection.Query.Querying;

/// <summary>
/// The values of the range variables during the evaluation of an expression: <c>$it</c>, the object that the query tests, and
/// the variables of the enclosing lambdas.
/// </summary>
internal sealed class QueryScope
{
    private readonly QueryScope? _parent;
    private readonly string _name;
    private readonly object? _value;

    public QueryScope( QueryContext context, object current )
    {
        this.Context = context;
        this._name = QueryNodes.ImplicitVariable;
        this._value = current;
    }

    private QueryScope( QueryScope parent, string name, object? value )
    {
        this.Context = parent.Context;
        this._parent = parent;
        this._name = name;
        this._value = value;
    }

    public QueryContext Context { get; }

    public QueryScope With( string name, object? value ) => new( this, name, value );

    public object? Get( string name )
    {
        for ( var scope = this; scope != null; scope = scope._parent )
        {
            if ( scope._name == name )
            {
                return scope._value;
            }
        }

        return null;
    }
}
