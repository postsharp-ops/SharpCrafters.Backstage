// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace SharpCrafters.Aspects.Introspection.Query.Querying;

/// <summary>
/// The static type of an expression: a kind, and the type of the schema of an object or a list of objects.
/// </summary>
internal readonly record struct ExpressionType( ValueKind Kind, string? TypeName = null )
{
    public static readonly ExpressionType Boolean = new( ValueKind.Boolean );
    public static readonly ExpressionType Integer = new( ValueKind.Integer );
    public static readonly ExpressionType String = new( ValueKind.String );

    // The type of the literal null, which compares with anything.
    public static readonly ExpressionType Null = new( ValueKind.Object, "null" );

    public bool IsNull => this.TypeName == "null";

    public bool IsCollection => this.Kind is ValueKind.ObjectList or ValueKind.StringList;

    public bool IsTextual => this.Kind is ValueKind.String or ValueKind.Enum;

    public override string ToString()
        => this.Kind switch
        {
            ValueKind.Object when this.IsNull => "null",
            ValueKind.Object => this.TypeName!,
            ValueKind.ObjectList => "collection of " + this.TypeName,
            ValueKind.StringList => "collection of string",
            ValueKind.Enum => "string",
            _ => this.Kind.ToString().ToLowerInvariant()
        };
}

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

internal sealed class LiteralExpression( object? value, ExpressionType type ) : QueryExpression( type )
{
    public object? Value { get; } = value;

    public override object? Evaluate( QueryContext context, object current, IReadOnlyDictionary<string, object?> variables ) => this.Value;
}

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

internal sealed class NotExpression( QueryExpression operand ) : QueryExpression( ExpressionType.Boolean )
{
    public override object? Evaluate( QueryContext context, object current, IReadOnlyDictionary<string, object?> variables )
        => operand.Evaluate( context, current, variables ) is not true;
}

internal sealed class LogicalExpression( QueryExpression left, bool isAnd, QueryExpression right ) : QueryExpression( ExpressionType.Boolean )
{
    public override object? Evaluate( QueryContext context, object current, IReadOnlyDictionary<string, object?> variables )
        => isAnd
            ? left.Evaluate( context, current, variables ) is true && right.Evaluate( context, current, variables ) is true
            : left.Evaluate( context, current, variables ) is true || right.Evaluate( context, current, variables ) is true;
}

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

internal sealed class InExpression( QueryExpression left, IReadOnlyList<object?> values ) : QueryExpression( ExpressionType.Boolean )
{
    public override object? Evaluate( QueryContext context, object current, IReadOnlyDictionary<string, object?> variables )
    {
        var value = left.Evaluate( context, current, variables );

        return values.Any( v => Equals( v, value ) );
    }
}

internal sealed class FunctionExpression : QueryExpression
{
    private static readonly TimeSpan _matchTimeout = TimeSpan.FromSeconds( 1 );

    private readonly string _name;
    private readonly IReadOnlyList<QueryExpression> _arguments;
    private readonly Regex? _pattern;

    public FunctionExpression( string name, IReadOnlyList<QueryExpression> arguments, ExpressionType type, Regex? pattern ) : base( type )
    {
        this._name = name;
        this._arguments = arguments;
        this._pattern = pattern;
    }

    public override object? Evaluate( QueryContext context, object current, IReadOnlyDictionary<string, object?> variables )
    {
        var values = this._arguments.Select( a => a.Evaluate( context, current, variables ) as string ).ToList();

        if ( values[0] == null )
        {
            return this.Type.Kind == ValueKind.Boolean ? false : null;
        }

        return this._name switch
        {
            "contains" => values[1] != null && values[0]!.Contains( values[1]!, StringComparison.Ordinal ),
            "startswith" => values[1] != null && values[0]!.StartsWith( values[1]!, StringComparison.Ordinal ),
            "endswith" => values[1] != null && values[0]!.EndsWith( values[1]!, StringComparison.Ordinal ),
            "matchesPattern" => Match( this._pattern!, values[0]! ),
            "tolower" => values[0]!.ToLowerInvariant(),
            _ => values[0]!.ToUpperInvariant()
        };

        static bool Match( Regex pattern, string value )
        {
            try
            {
                return pattern.IsMatch( value );
            }
            catch ( RegexMatchTimeoutException )
            {
                throw new IntrospectionCommandException( $"The regular expression '{pattern}' takes too long to match.", IntrospectionExitCodes.InvalidQuery );
            }
        }
    }

    public static Regex CreatePattern( string pattern )
    {
        try
        {
            return new Regex( pattern, RegexOptions.CultureInvariant, _matchTimeout );
        }
        catch ( ArgumentException e )
        {
            throw new IntrospectionCommandException( $"'{pattern}' is not a valid regular expression: {e.Message}", IntrospectionExitCodes.InvalidQuery );
        }
    }

    public static string FormatValue( object? value )
        => value switch
        {
            null => "null",
            string s => s,
            bool b => b ? "true" : "false",
            long l => l.ToString( CultureInfo.InvariantCulture ),
            _ => value.ToString() ?? ""
        };
}
