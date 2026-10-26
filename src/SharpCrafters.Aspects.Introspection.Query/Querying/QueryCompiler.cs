// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using Microsoft.OData.Edm;
using Microsoft.OData.UriParser;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace SharpCrafters.Aspects.Introspection.Query.Querying;

/// <summary>
/// Compiles a tree that Microsoft.OData.Core binds into a delegate that evaluates it against the objects of the model. A node
/// or a function that the evaluator does not support is an invalid query, reported before any file is read.
/// </summary>
/// <remarks>
/// <para>
/// Integers are compared as <see cref="long"/>; an empty string is <c>null</c>, as an absent attribute of the file; an ordering
/// comparison with <c>null</c> is false, and <c>eq</c> and <c>ne</c> compare with <c>null</c>; the string functions are ordinal.
/// </para>
/// </remarks>
internal sealed class QueryCompiler
{
    private static readonly TimeSpan _matchTimeout = TimeSpan.FromSeconds( 1 );

    /// <summary>
    /// Gets the functions that the evaluator supports.
    /// </summary>
    public static IReadOnlyList<string> Functions { get; } =
        ["contains", "startswith", "endswith", "matchesPattern", "indexof", "length", "substring", "concat", "tolower", "toupper", "trim"];

    private readonly QueryText _query;
    private readonly IntrospectionUriResolver _resolver;

    public QueryCompiler( QueryText query, IntrospectionUriResolver resolver )
    {
        this._query = query;
        this._resolver = resolver;
    }

    public QueryExpression Compile( SingleValueNode node ) => new( this.CompileValue( node ) );

    private Func<QueryScope, object?> CompileValue( SingleValueNode node )
    {
        switch ( node )
        {
            case ConstantNode constant:
                var value = QueryValues.Normalize( constant.Value );

                return _ => value;

            case ConvertNode convert:
                return this.CompileValue( convert.Source );

            case ResourceRangeVariableReferenceNode variable:
                var resourceName = variable.Name;

                return scope => scope.Get( resourceName );

            case NonResourceRangeVariableReferenceNode variable:
                var name = variable.Name;

                return scope => QueryValues.Normalize( scope.Get( name ) );

            case SingleValuePropertyAccessNode property:
                return this.CompileField( property.Source, property.Property );

            case SingleNavigationNode navigation:
                return this.CompileField( navigation.Source, navigation.NavigationProperty );

            case UnaryOperatorNode unary:
                return this.CompileUnary( unary );

            case BinaryOperatorNode binary:
                return this.CompileBinary( binary );

            case InNode inNode:
                return this.CompileIn( inNode );

            case AnyNode any:
                return this.CompileLambda( any.Source, any.CurrentRangeVariable, any.Body, isAll: false );

            case AllNode all:
                return this.CompileLambda( all.Source, all.CurrentRangeVariable, all.Body, isAll: true );

            case CountNode count:
                return this.CompileCount( count );

            case SingleValueFunctionCallNode function:
                return this.CompileFunction( function );

            default:
                throw this.Unsupported( node );
        }
    }

    private Func<QueryScope, IEnumerable?> CompileCollection( CollectionNode node )
    {
        switch ( node )
        {
            case CollectionNavigationNode navigation:
                var getNavigation = this.CompileField( navigation.Source, navigation.NavigationProperty );

                return scope => getNavigation( scope ) as IEnumerable;

            case CollectionPropertyAccessNode property:
                var getProperty = this.CompileField( property.Source, property.Property );

                return scope => getProperty( scope ) as IEnumerable;

            case CollectionConstantNode constant:
                var values = constant.Collection.Select( c => QueryValues.Normalize( c.Value ) ).ToList();

                return _ => values;

            default:
                throw this.Unsupported( node );
        }
    }

    private Func<QueryScope, object?> CompileField( QueryNode source, IEdmProperty property )
    {
        var getSource = this.CompileValue( (SingleValueNode) source );
        var field = IntrospectionEdmModel.Instance.GetField( property );

        return scope =>
        {
            var value = getSource( scope );

            return value == null ? null : QueryValues.Normalize( field.GetValue( scope.Context, value ) );
        };
    }

    private Func<QueryScope, object?> CompileUnary( UnaryOperatorNode node )
    {
        var operand = this.CompileValue( node.Operand );

        return node.OperatorKind switch
        {
            UnaryOperatorKind.Not => scope => operand( scope ) is not true,
            UnaryOperatorKind.Negate => scope => operand( scope ) switch
            {
                long l => -l,
                double d => -d,
                _ => null
            },
            _ => throw this.Unsupported( node )
        };
    }

    private Func<QueryScope, object?> CompileBinary( BinaryOperatorNode node )
    {
        var left = this.CompileValue( node.Left );
        var right = this.CompileValue( node.Right );

        switch ( node.OperatorKind )
        {
            case BinaryOperatorKind.And:
                return scope => left( scope ) is true && right( scope ) is true;

            case BinaryOperatorKind.Or:
                return scope => left( scope ) is true || right( scope ) is true;

            case BinaryOperatorKind.Equal:
                return scope => QueryValues.AreEqual( left( scope ), right( scope ) );

            case BinaryOperatorKind.NotEqual:
                return scope => !QueryValues.AreEqual( left( scope ), right( scope ) );

            case BinaryOperatorKind.GreaterThan:
                return Ordering( c => c > 0 );

            case BinaryOperatorKind.GreaterThanOrEqual:
                return Ordering( c => c >= 0 );

            case BinaryOperatorKind.LessThan:
                return Ordering( c => c < 0 );

            case BinaryOperatorKind.LessThanOrEqual:
                return Ordering( c => c <= 0 );

            case BinaryOperatorKind.Add:
                return Arithmetic( ( a, b ) => a + b, ( a, b ) => a + b );

            case BinaryOperatorKind.Subtract:
                return Arithmetic( ( a, b ) => a - b, ( a, b ) => a - b );

            case BinaryOperatorKind.Multiply:
                return Arithmetic( ( a, b ) => a * b, ( a, b ) => a * b );

            case BinaryOperatorKind.Divide:
                return Arithmetic( ( a, b ) => b == 0 ? null : a / b, ( a, b ) => a / b );

            case BinaryOperatorKind.Modulo:
                return Arithmetic( ( a, b ) => b == 0 ? null : a % b, ( a, b ) => a % b );

            default:
                throw this.Unsupported( node );
        }

        // An ordering with null is false, as in OData.
        Func<QueryScope, object?> Ordering( Func<int, bool> test )
            => scope =>
            {
                var l = left( scope );
                var r = right( scope );

                return l != null && r != null && test( QueryValues.Compare( l, r ) );
            };

        Func<QueryScope, object?> Arithmetic( Func<long, long, long?> integer, Func<double, double, double> real )
            => scope => (left( scope ), right( scope )) switch
            {
                (long a, long b) => integer( a, b ),
                (long a, double b) => real( a, b ),
                (double a, long b) => real( a, b ),
                (double a, double b) => real( a, b ),
                _ => null
            };
    }

    private Func<QueryScope, object?> CompileIn( InNode node )
    {
        // An enumeration value that does not exist is a typo, not a condition that is never true.
        if ( node.Right is CollectionConstantNode constants )
        {
            foreach ( var constant in constants.Collection )
            {
                this._resolver.CheckEnumValue( node.Left, constant );
            }
        }

        var left = this.CompileValue( node.Left );
        var right = this.CompileCollection( node.Right );

        return scope =>
        {
            var value = left( scope );

            return right( scope )?.Cast<object?>().Any( v => QueryValues.AreEqual( QueryValues.Normalize( v ), value ) ) == true;
        };
    }

    private Func<QueryScope, object?> CompileLambda( CollectionNode source, RangeVariable? variable, SingleValueNode? body, bool isAll )
    {
        var items = this.CompileCollection( source );

        // any() without a lambda tests for a non-empty collection.
        if ( variable == null || body == null )
        {
            return scope => items( scope ) is { } list ? isAll || list.Cast<object?>().Any() : isAll;
        }

        var test = this.CompileValue( body );
        var name = variable.Name;

        // A null collection has no item: any is false and all is true.
        return scope => items( scope ) is not { } list
            ? isAll
            : isAll
                ? list.Cast<object?>().All( item => test( scope.With( name, item ) ) is true )
                : list.Cast<object?>().Any( item => test( scope.With( name, item ) ) is true );
    }

    private Func<QueryScope, object?> CompileCount( CountNode node )
    {
        if ( node.SearchClause != null )
        {
            throw this._query.Error( $"$search in '{QueryNodes.Describe( node )}' is not supported." );
        }

        var items = this.CompileCollection( node.Source );

        if ( node.FilterClause == null )
        {
            return scope => (long) (items( scope )?.Cast<object>().Count() ?? 0);
        }

        var filter = this.CompileValue( node.FilterClause.Expression );
        var name = node.FilterClause.RangeVariable.Name;

        return scope => (long) (items( scope )?.Cast<object?>().Count( item => filter( scope.With( name, item ) ) is true ) ?? 0);
    }

    private Func<QueryScope, object?> CompileFunction( SingleValueFunctionCallNode node )
    {
        var arguments = node.Parameters.Select( p => p is SingleValueNode value ? this.CompileValue( value ) : throw this.Unsupported( p ) ).ToList();

        string? Text( QueryScope scope, int index ) => arguments[index]( scope ) as string;

        long? Integer( QueryScope scope, int index ) => arguments[index]( scope ) as long?;

        switch ( node.Name )
        {
            case "contains" when arguments.Count == 2:
                return scope => Text( scope, 0 ) is { } s && Text( scope, 1 ) is { } t && s.Contains( t, StringComparison.Ordinal );

            case "startswith" when arguments.Count == 2:
                return scope => Text( scope, 0 ) is { } s && Text( scope, 1 ) is { } t && s.StartsWith( t, StringComparison.Ordinal );

            case "endswith" when arguments.Count == 2:
                return scope => Text( scope, 0 ) is { } s && Text( scope, 1 ) is { } t && s.EndsWith( t, StringComparison.Ordinal );

            case "indexof" when arguments.Count == 2:
                return scope => Text( scope, 0 ) is { } s && Text( scope, 1 ) is { } t ? (long) s.IndexOf( t, StringComparison.Ordinal ) : null;

            case "length" when arguments.Count == 1:
                return scope => Text( scope, 0 ) is { } s ? (long) s.Length : null;

            case "substring" when arguments.Count is 2 or 3:
                return scope =>
                {
                    if ( Text( scope, 0 ) is not { } s || Integer( scope, 1 ) is not { } start )
                    {
                        return null;
                    }

                    var from = (int) Math.Clamp( start, 0, s.Length );
                    var length = arguments.Count == 3 ? Integer( scope, 2 ) : s.Length - from;

                    return length == null ? null : QueryValues.Normalize( s.Substring( from, (int) Math.Clamp( length.Value, 0, s.Length - from ) ) );
                };

            case "concat" when arguments.Count == 2:
                return scope => Text( scope, 0 ) is { } s && Text( scope, 1 ) is { } t ? QueryValues.Normalize( s + t ) : null;

            case "tolower" when arguments.Count == 1:
                return scope => Text( scope, 0 )?.ToLowerInvariant();

            case "toupper" when arguments.Count == 1:
                return scope => Text( scope, 0 )?.ToUpperInvariant();

            case "trim" when arguments.Count == 1:
                return scope => QueryValues.Normalize( Text( scope, 0 )?.Trim() );

            case "matchesPattern" when arguments.Count == 2:
                var pattern = QueryNodes.StripConvert( (SingleValueNode) node.Parameters.ElementAt( 1 ) ) is ConstantNode { Value: string text }
                    ? this.CreatePattern( text )
                    : throw this._query.Error( "The pattern of 'matchesPattern' must be a string literal." );

                return scope => Text( scope, 0 ) is { } s && this.Match( pattern, s );

            default:
                throw this._query.Error(
                    Functions.Contains( node.Name )
                        ? $"The function '{node.Name}' does not take {arguments.Count} argument(s)."
                        : $"The function '{node.Name}' is not supported. The supported functions are: {string.Join( ", ", Functions )}." );
        }
    }

    private Regex CreatePattern( string pattern )
    {
        try
        {
            return new Regex( pattern, RegexOptions.CultureInvariant, _matchTimeout );
        }
        catch ( ArgumentException e )
        {
            throw this._query.Error( $"'{pattern}' is not a valid regular expression: {e.Message}" );
        }
    }

    private bool Match( Regex pattern, string value )
    {
        try
        {
            return pattern.IsMatch( value );
        }
        catch ( RegexMatchTimeoutException )
        {
            throw this._query.Error( $"The regular expression '{pattern}' takes too long to match." );
        }
    }

    private IntrospectionCommandException Unsupported( QueryNode node )
        => this._query.Error(
            node switch
            {
                BinaryOperatorNode binary => $"The operator '{binary.OperatorKind}' is not supported.",
                UnaryOperatorNode unary => $"The operator '{unary.OperatorKind}' is not supported.",
                _ => $"'{QueryNodes.Describe( node )}' ({node.Kind}) is not supported."
            } );
}
