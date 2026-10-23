// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace SharpCrafters.Aspects.Introspection.Query.Querying;

/// <summary>
/// Parses the conditions of <c>--where</c>, the paths of <c>--select</c> and the orderings of <c>--order-by</c>, and binds them to
/// the schema before the query runs, so that an error states its position and the valid fields at that point
/// (Documentation/future/Introspection-Cli.md of PostSharp, section 5.4).
/// </summary>
/// <remarks>
/// <para>The subset of OData 4.01:</para>
/// <list type="bullet">
/// <item>Paths: fields separated by <c>/</c>, as named in the JSON output; <c>path/$count</c> for the size of a collection.</item>
/// <item>Literals: <c>'string'</c> (a quote is doubled), integers, <c>true</c>, <c>false</c>, <c>null</c>; enumeration values as
/// strings.</item>
/// <item>Operators: <c>eq</c>, <c>ne</c>, <c>gt</c>, <c>ge</c>, <c>lt</c>, <c>le</c>, <c>in (...)</c>, <c>and</c>, <c>or</c>,
/// <c>not</c>, parentheses.</item>
/// <item>Functions: <c>contains</c>, <c>startswith</c>, <c>endswith</c>, <c>matchesPattern</c>, <c>tolower</c>,
/// <c>toupper</c>.</item>
/// <item>Collections: <c>path/any(x: condition)</c>, <c>path/all(x: condition)</c>, <c>path/any()</c>.</item>
/// </list>
/// <para>
/// Keywords are lower case, and field names and comparisons are case-sensitive, as in OData. <c>$count</c> is kept, as OData
/// writes it, although shells expand <c>$</c> inside double quotes: a path that ends with <c>/</c> is reported with that cause,
/// and the help says to quote the condition with single quotes or to escape the <c>$</c>.
/// </para>
/// </remarks>
internal sealed class QueryParser
{
    private static readonly HashSet<string> _comparisonOperators = new( StringComparer.Ordinal ) { "eq", "ne", "gt", "ge", "lt", "le" };

    private const string _shellHint =
        "If the path ended with $count inside double quotes, the shell removed it: quote the condition with single quotes, or escape the $ "
        + "(\\$count in bash, `$count in PowerShell).";

    private static readonly Dictionary<string, (int Arity, bool IsPredicate)> _functions = new( StringComparer.Ordinal )
    {
        ["contains"] = (2, true),
        ["startswith"] = (2, true),
        ["endswith"] = (2, true),
        ["matchesPattern"] = (2, true),
        ["tolower"] = (1, false),
        ["toupper"] = (1, false)
    };

    private readonly string _text;
    private readonly string _option;
    private readonly TypeDefinition _type;
    private readonly IntrospectionProvider _provider;
    private readonly List<Token> _tokens;
    private readonly Dictionary<string, ExpressionType> _variables = new( StringComparer.Ordinal );
    private int _position;

    private QueryParser( string text, string option, TypeDefinition type, IntrospectionProvider provider )
    {
        this._text = text;
        this._option = option;
        this._type = type;
        this._provider = provider;
        this._tokens = this.Tokenize();
    }

    /// <summary>
    /// Parses a condition of <c>--where</c>.
    /// </summary>
    public static QueryExpression ParseCondition( string text, TypeDefinition type, IntrospectionProvider provider )
    {
        var parser = new QueryParser( text, "--where", type, provider );
        var expression = parser.ParseOr();
        parser.ExpectEnd();

        if ( expression.Type.Kind != ValueKind.Boolean )
        {
            throw parser.Error( $"The condition is a {expression.Type}, not a Boolean.", 0 );
        }

        return expression;
    }

    /// <summary>
    /// Parses the paths of <c>--select</c>, separated by commas.
    /// </summary>
    public static IReadOnlyList<PathExpression> ParsePaths( string text, string option, TypeDefinition type, IntrospectionProvider provider )
    {
        var parser = new QueryParser( text, option, type, provider );
        var paths = new List<PathExpression>();

        do
        {
            paths.Add( parser.ParsePath() );
        }
        while ( parser.TryConsume( TokenKind.Comma ) );

        parser.ExpectEnd();

        return paths;
    }

    /// <summary>
    /// Parses the orderings of <c>--order-by</c>: scalar paths, each followed by <c>asc</c> or <c>desc</c>, separated by commas.
    /// </summary>
    public static IReadOnlyList<(PathExpression Path, bool IsDescending)> ParseOrderings( string text, TypeDefinition type, IntrospectionProvider provider )
    {
        var parser = new QueryParser( text, "--order-by", type, provider );
        var orderings = new List<(PathExpression, bool)>();

        do
        {
            var start = parser.Current.Position;
            var path = parser.ParsePath();

            if ( path.Type.Kind is ValueKind.Object or ValueKind.ObjectList or ValueKind.StringList )
            {
                throw parser.Error( $"'{path.Text}' is a {path.Type}; only a string, an integer or a Boolean can be ordered.", start );
            }

            var isDescending = false;

            if ( parser.Current is { Kind: TokenKind.Identifier, Text: "asc" or "desc" } )
            {
                isDescending = parser.Current.Text == "desc";
                parser._position++;
            }

            orderings.Add( (path, isDescending) );
        }
        while ( parser.TryConsume( TokenKind.Comma ) );

        parser.ExpectEnd();

        return orderings;
    }

    private Token Current => this._tokens[this._position];

    private QueryExpression ParseOr()
    {
        var left = this.ParseAnd();

        while ( this.TryConsumeKeyword( "or" ) )
        {
            left = new LogicalExpression( this.RequireBoolean( left ), false, this.RequireBoolean( this.ParseAnd() ) );
        }

        return left;
    }

    private QueryExpression ParseAnd()
    {
        var left = this.ParseNot();

        while ( this.TryConsumeKeyword( "and" ) )
        {
            left = new LogicalExpression( this.RequireBoolean( left ), true, this.RequireBoolean( this.ParseNot() ) );
        }

        return left;
    }

    private QueryExpression ParseNot()
    {
        if ( this.TryConsumeKeyword( "not" ) )
        {
            return new NotExpression( this.RequireBoolean( this.ParseNot() ) );
        }

        return this.ParseComparison();
    }

    private QueryExpression ParseComparison()
    {
        var start = this.Current.Position;
        var left = this.ParsePrimary();

        if ( this.Current is { Kind: TokenKind.Identifier } token && _comparisonOperators.Contains( token.Text ) )
        {
            this._position++;
            var rightStart = this.Current.Position;
            var right = this.ParsePrimary();
            this.CheckComparable( left, token.Text, right, start, rightStart );

            return new ComparisonExpression( left, token.Text, right );
        }

        if ( this.TryConsumeKeyword( "in" ) )
        {
            this.Expect( TokenKind.OpenParenthesis, "'(' after 'in'" );
            var values = new List<object?>();

            do
            {
                var valueStart = this.Current.Position;
                var value = this.ParsePrimary();

                if ( value is not LiteralExpression literal )
                {
                    throw this.Error( "The list of 'in' holds only literals.", valueStart );
                }

                this.CheckComparable( left, "eq", value, start, valueStart );
                values.Add( literal.Value );
            }
            while ( this.TryConsume( TokenKind.Comma ) );

            this.Expect( TokenKind.CloseParenthesis, "')' at the end of the list of 'in'" );

            return new InExpression( left, values );
        }

        return left;
    }

    private QueryExpression ParsePrimary()
    {
        var token = this.Current;

        switch ( token.Kind )
        {
            case TokenKind.OpenParenthesis:
                this._position++;
                var inner = this.ParseOr();
                this.Expect( TokenKind.CloseParenthesis, "')'" );

                return inner;

            case TokenKind.String:
                this._position++;

                return new LiteralExpression( token.Text, ExpressionType.String );

            case TokenKind.Number:
                this._position++;

                return new LiteralExpression( long.Parse( token.Text, CultureInfo.InvariantCulture ), ExpressionType.Integer );

            case TokenKind.Identifier when token.Text is "true" or "false":
                this._position++;

                return new LiteralExpression( token.Text == "true", ExpressionType.Boolean );

            case TokenKind.Identifier when token.Text == "null":
                this._position++;

                return new LiteralExpression( null, ExpressionType.Null );

            case TokenKind.Identifier when this._tokens[this._position + 1].Kind == TokenKind.OpenParenthesis:
                return this.ParseFunction();

            case TokenKind.Identifier:
                return this.ParsePathOrLambda();

            default:
                throw this.Error( $"Expected a field, a literal or '(' but found {Describe( token )}.", token.Position );
        }
    }

    private QueryExpression ParseFunction()
    {
        var name = this.Current;
        this._position += 2;

        if ( !_functions.TryGetValue( name.Text, out var function ) )
        {
            throw this.Error( $"'{name.Text}' is not a function. The functions are {string.Join( ", ", _functions.Keys )}.", name.Position );
        }

        var arguments = new List<QueryExpression>();

        if ( this.Current.Kind != TokenKind.CloseParenthesis )
        {
            do
            {
                var start = this.Current.Position;
                var argument = this.ParseOr();

                if ( !argument.Type.IsTextual && !argument.Type.IsNull )
                {
                    throw this.Error( $"The arguments of '{name.Text}' are strings, not a {argument.Type}.", start );
                }

                arguments.Add( argument );
            }
            while ( this.TryConsume( TokenKind.Comma ) );
        }

        this.Expect( TokenKind.CloseParenthesis, $"')' at the end of the arguments of '{name.Text}'" );

        if ( arguments.Count != function.Arity )
        {
            throw this.Error( $"'{name.Text}' takes {function.Arity} argument(s), not {arguments.Count}.", name.Position );
        }

        var pattern = name.Text == "matchesPattern"
            ? arguments[1] is LiteralExpression { Value: string text }
                ? FunctionExpression.CreatePattern( text )
                : throw this.Error( "The pattern of 'matchesPattern' must be a string literal.", name.Position )
            : null;

        return new FunctionExpression( name.Text, arguments, function.IsPredicate ? ExpressionType.Boolean : ExpressionType.String, pattern );
    }

    private QueryExpression ParsePathOrLambda()
    {
        var path = this.ParsePath( allowLambda: true, out var lambda );

        return lambda ?? path;
    }

    private PathExpression ParsePath() => this.ParsePath( allowLambda: false, out _ );

    private PathExpression ParsePath( bool allowLambda, out QueryExpression? lambda )
    {
        lambda = null;
        var start = this.Current.Position;
        var first = this.Expect( TokenKind.Identifier, "a field" );

        string? variable = null;
        ExpressionType type;
        var fields = new List<FieldDefinition>();

        if ( this._variables.TryGetValue( first.Text, out var variableType ) )
        {
            variable = first.Text;
            type = variableType;
        }
        else
        {
            var field = this.GetField( this._type, first );
            fields.Add( field );
            type = TypeOf( field );
        }

        var isCount = false;

        while ( this.TryConsume( TokenKind.Slash ) )
        {
            var segment = this.Current;

            if ( segment.Kind != TokenKind.Identifier )
            {
                throw this.Error( $"Expected a field after '/' but found {Describe( segment )}. {_shellHint}", segment.Position );
            }

            if ( type.IsCollection )
            {
                this._position++;

                if ( segment.Text == "$count" )
                {
                    isCount = true;
                    type = ExpressionType.Integer;

                    break;
                }

                if ( segment.Text is "any" or "all" && allowLambda )
                {
                    var path = new PathExpression( variable, fields, false, type, this._text.Substring( start, segment.Position - start - 1 ) );
                    lambda = this.ParseLambda( path, segment.Text == "all", segment );

                    return path;
                }

                // 'transformations/ gt 1' is what a shell leaves of 'transformations/$count gt 1' inside double quotes.
                var hint = _comparisonOperators.Contains( segment.Text ) ? " " + _shellHint : "";

                throw this.Error(
                    $"'{this.PathText( start, segment.Position )}' is a {type}: it is followed by $count, any(...) or all(...), not by '{segment.Text}'.{hint}",
                    segment.Position );
            }

            if ( type.Kind != ValueKind.Object || type.IsNull )
            {
                throw this.Error( $"'{this.PathText( start, segment.Position )}' is a {type}, which has no field.", segment.Position );
            }

            this._position++;
            var next = this.GetField( IntrospectionSchema.GetType( type.TypeName! ), segment );
            fields.Add( next );
            type = TypeOf( next );
        }

        return new PathExpression( variable, fields, isCount, type, this._text.Substring( start, this.Current.Position - start ).TrimEnd() );
    }

    private QueryExpression ParseLambda( PathExpression collection, bool isAll, Token keyword )
    {
        this.Expect( TokenKind.OpenParenthesis, $"'(' after '{keyword.Text}'" );

        if ( !isAll && this.TryConsume( TokenKind.CloseParenthesis ) )
        {
            return new LambdaExpression( collection, false, null, null );
        }

        var variable = this.Expect( TokenKind.Identifier, "the name of the variable of the lambda" );
        this.Expect( TokenKind.Colon, $"':' after the variable '{variable.Text}'" );

        if ( this._variables.ContainsKey( variable.Text ) || this._type.GetField( variable.Text ) != null )
        {
            throw this.Error( $"The variable '{variable.Text}' hides another variable or a field.", variable.Position );
        }

        var itemType = collection.Type.Kind == ValueKind.StringList ? ExpressionType.String : new ExpressionType( ValueKind.Object, collection.Type.TypeName );
        this._variables.Add( variable.Text, itemType );

        var bodyStart = this.Current.Position;
        var body = this.ParseOr();
        this._variables.Remove( variable.Text );

        if ( body.Type.Kind != ValueKind.Boolean )
        {
            throw this.Error( $"The condition of '{keyword.Text}' is a {body.Type}, not a Boolean.", bodyStart );
        }

        this.Expect( TokenKind.CloseParenthesis, $"')' at the end of '{keyword.Text}'" );

        return new LambdaExpression( collection, isAll, variable.Text, body );
    }

    private FieldDefinition GetField( TypeDefinition? type, Token token )
    {
        if ( type == null )
        {
            throw this.Error( $"'{token.Text}' is not a field: the variable before it is a string.", token.Position );
        }

        var field = type.GetField( token.Text );

        if ( field == null )
        {
            var suggestion = type.Fields.FirstOrDefault( f => string.Equals( f.Name, token.Text, StringComparison.OrdinalIgnoreCase ) );

            throw this.Error(
                $"'{token.Text}' is not a field of {type.Name}."
                + (suggestion != null ? $" Field names are case-sensitive: did you mean '{suggestion.Name}'?" : "")
                + $" The fields of {type.Name} are: {string.Join( ", ", type.Fields.Select( f => f.Name ) )}.",
                token.Position );
        }

        if ( !this._provider.Provides( type.Name, field.Name ) )
        {
            throw this.Error( $"The files of {this._provider.ProductName} do not record '{field.Name}' of {type.Name}.", token.Position );
        }

        return field;
    }

    private static ExpressionType TypeOf( FieldDefinition field ) => new( field.Kind, field.TypeName );

    private void CheckComparable( QueryExpression left, string op, QueryExpression right, int leftStart, int rightStart )
    {
        var l = left.Type;
        var r = right.Type;

        if ( op is "eq" or "ne" && (l.IsNull || r.IsNull) )
        {
            if ( l.IsCollection || r.IsCollection )
            {
                throw this.Error( "A collection is never null: use $count or any().", l.IsCollection ? leftStart : rightStart );
            }

            return;
        }

        var compatible = (l.IsTextual && r.IsTextual) || (l.Kind == r.Kind && l.Kind is ValueKind.Integer or ValueKind.Boolean);

        if ( !compatible || (op is not ("eq" or "ne") && l.Kind == ValueKind.Boolean) )
        {
            throw this.Error( $"A {l} cannot be compared with a {r} by '{op}'.", rightStart );
        }

        // An enumeration value that does not exist is a typo, not a condition that is never true.
        CheckEnumValue( left, right, rightStart );
        CheckEnumValue( right, left, leftStart );

        void CheckEnumValue( QueryExpression field, QueryExpression literal, int position )
        {
            if ( field is PathExpression { Fields.Count: > 0 } path && path.Fields[^1].EnumValues is { } values && !path.IsCount
                 && literal is LiteralExpression { Value: string value } && !values.Contains( value, StringComparer.Ordinal ) )
            {
                throw this.Error( $"'{value}' is not a value of '{path.Text}'. The values are: {string.Join( ", ", values )}.", position );
            }
        }
    }

    private QueryExpression RequireBoolean( QueryExpression expression )
        => expression.Type.Kind == ValueKind.Boolean ? expression : throw this.Error( $"Expected a condition but found a {expression.Type}.", this.Current.Position );

    private bool TryConsumeKeyword( string keyword )
    {
        if ( this.Current is { Kind: TokenKind.Identifier } token && token.Text == keyword )
        {
            this._position++;

            return true;
        }

        return false;
    }

    private bool TryConsume( TokenKind kind )
    {
        if ( this.Current.Kind == kind )
        {
            this._position++;

            return true;
        }

        return false;
    }

    private Token Expect( TokenKind kind, string what )
    {
        var token = this.Current;

        if ( token.Kind != kind )
        {
            throw this.Error( $"Expected {what} but found {Describe( token )}.", token.Position );
        }

        this._position++;

        return token;
    }

    private void ExpectEnd()
    {
        if ( this.Current.Kind != TokenKind.End )
        {
            throw this.Error( $"Unexpected {Describe( this.Current )}.", this.Current.Position );
        }
    }

    private string PathText( int start, int end ) => this._text.Substring( start, Math.Max( 0, end - start - 1 ) );

    private static string Describe( Token token )
        => token.Kind switch
        {
            TokenKind.End => "the end of the expression",
            TokenKind.String => $"the string '{token.Text}'",
            _ => $"'{token.Text}'"
        };

    private IntrospectionCommandException Error( string message, int position )
        => new(
            $"{this._option}: {message}" + Environment.NewLine + "  " + this._text + Environment.NewLine + "  " + new string( ' ', Math.Min( position, this._text.Length ) ) + "^",
            IntrospectionExitCodes.InvalidQuery );

    private List<Token> Tokenize()
    {
        var tokens = new List<Token>();
        var text = this._text;
        var i = 0;

        while ( i < text.Length )
        {
            var c = text[i];

            if ( char.IsWhiteSpace( c ) )
            {
                i++;

                continue;
            }

            var start = i;

            switch ( c )
            {
                case '(':
                    tokens.Add( new Token( TokenKind.OpenParenthesis, "(", start ) );
                    i++;

                    continue;

                case ')':
                    tokens.Add( new Token( TokenKind.CloseParenthesis, ")", start ) );
                    i++;

                    continue;

                case ',':
                    tokens.Add( new Token( TokenKind.Comma, ",", start ) );
                    i++;

                    continue;

                case '/':
                    tokens.Add( new Token( TokenKind.Slash, "/", start ) );
                    i++;

                    continue;

                case ':':
                    tokens.Add( new Token( TokenKind.Colon, ":", start ) );
                    i++;

                    continue;

                case '\'':
                    var value = new StringBuilder();
                    i++;

                    while ( true )
                    {
                        if ( i >= text.Length )
                        {
                            throw this.Error( "The string is not closed.", start );
                        }

                        if ( text[i] == '\'' )
                        {
                            if ( i + 1 < text.Length && text[i + 1] == '\'' )
                            {
                                value.Append( '\'' );
                                i += 2;

                                continue;
                            }

                            i++;

                            break;
                        }

                        value.Append( text[i++] );
                    }

                    tokens.Add( new Token( TokenKind.String, value.ToString(), start ) );

                    continue;
            }

            if ( char.IsDigit( c ) || (c == '-' && i + 1 < text.Length && char.IsDigit( text[i + 1] )) )
            {
                i++;

                while ( i < text.Length && char.IsDigit( text[i] ) )
                {
                    i++;
                }

                tokens.Add( new Token( TokenKind.Number, text.Substring( start, i - start ), start ) );

                continue;
            }

            if ( char.IsLetter( c ) || c is '_' or '$' )
            {
                i++;

                while ( i < text.Length && (char.IsLetterOrDigit( text[i] ) || text[i] == '_') )
                {
                    i++;
                }

                tokens.Add( new Token( TokenKind.Identifier, text.Substring( start, i - start ), start ) );

                continue;
            }

            throw this.Error( $"Unexpected character '{c}'.", start );
        }

        tokens.Add( new Token( TokenKind.End, "", text.Length ) );

        return tokens;
    }

    private enum TokenKind
    {
        Identifier,
        String,
        Number,
        OpenParenthesis,
        CloseParenthesis,
        Comma,
        Slash,
        Colon,
        End
    }

    private sealed record Token( TokenKind Kind, string Text, int Position );
}
