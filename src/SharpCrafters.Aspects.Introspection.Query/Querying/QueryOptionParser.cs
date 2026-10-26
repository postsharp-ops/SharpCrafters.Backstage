// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using Microsoft.OData;
using Microsoft.OData.UriParser;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace SharpCrafters.Aspects.Introspection.Query.Querying;

/// <summary>
/// Parses the conditions of <c>--where</c>, the paths of <c>--select</c> and the orderings of <c>--order-by</c>, and binds them to
/// the schema before the query runs (Documentation/future/Introspection-Cli.md of PostSharp, section 5.4).
/// </summary>
/// <remarks>
/// <para>
/// <c>--where</c> takes the whole OData 4.01 <c>$filter</c> grammar and <c>--order-by</c> the <c>$orderby</c> grammar, which
/// Microsoft.OData.Core parses and binds against <see cref="IntrospectionEdmModel"/>; <see cref="QueryCompiler"/> evaluates
/// them and refuses the constructs it does not support. <c>--select</c> takes paths of fields separated by <c>/</c>, of which
/// the last can be <c>$count</c> after a collection, which the OData <c>$select</c> cannot express.
/// </para>
/// <para>
/// Keywords are lower case, and field names and comparisons are case-sensitive, as in OData. <c>$count</c> is kept, as OData
/// writes it, although shells expand <c>$</c> inside double quotes: a path that ends with <c>/</c> is reported with that cause.
/// </para>
/// </remarks>
internal static partial class QueryOptionParser
{
    // A string literal, which the detection of a stripped $count skips.
    [GeneratedRegex( "'(?:[^']|'')*'?", RegexOptions.CultureInvariant )]
    private static partial Regex StringLiteralRegex();

    // What a shell leaves of 'transformations/$count gt 1' inside double quotes: 'transformations/ gt 1'. A '/' followed by a
    // space, a comma, a parenthesis or the end is never valid.
    [GeneratedRegex( @"/(?:\s*$|\s*[,)]|\s+(?<next>\w+))", RegexOptions.CultureInvariant )]
    private static partial Regex StrippedCountRegex();

    // The position that Microsoft.OData.Core gives in the message of a syntax error.
    [GeneratedRegex( @"at position (?<position>\d+)", RegexOptions.CultureInvariant )]
    private static partial Regex PositionRegex();

    /// <summary>
    /// Parses a condition of <c>--where</c>.
    /// </summary>
    public static QueryExpression ParseCondition( string text, TypeDefinition type, IntrospectionProvider provider )
    {
        var query = new QueryText( "--where", text );
        CheckStrippedCount( query );

        return Parse(
            query,
            type,
            provider,
            "$filter",
            ( parser, compiler ) => compiler.Compile( parser.ParseFilter()?.Expression ?? throw query.Error( "The condition is empty." ) ) );
    }

    /// <summary>
    /// Parses the orderings of <c>--order-by</c>: expressions, each optionally followed by <c>asc</c> or <c>desc</c>, separated by
    /// commas.
    /// </summary>
    public static IReadOnlyList<(QueryExpression Expression, bool IsDescending)> ParseOrderings( string text, TypeDefinition type, IntrospectionProvider provider )
    {
        var query = new QueryText( "--order-by", text );
        CheckStrippedCount( query );

        return Parse(
            query,
            type,
            provider,
            "$orderby",
            ( parser, compiler ) =>
            {
                var orderings = new List<(QueryExpression, bool)>();

                for ( var clause = parser.ParseOrderBy(); clause != null; clause = clause.ThenBy )
                {
                    orderings.Add( (compiler.Compile( clause.Expression ), clause.Direction == OrderByDirection.Descending) );
                }

                return orderings;
            } );
    }

    /// <summary>
    /// Parses the paths of <c>--select</c>, separated by commas.
    /// </summary>
    public static IReadOnlyList<FieldPath> ParsePaths( string text, string option, TypeDefinition type, IntrospectionProvider provider )
    {
        var query = new QueryText( option, text );
        var resolver = new IntrospectionUriResolver( query, provider );
        var paths = new List<FieldPath>();
        var start = 0;

        foreach ( var item in text.Split( ',' ) )
        {
            var path = ParsePath( query, resolver, type, item, start );

            // An object and one of its fields would be two writes of the same property: say so rather than drop one.
            foreach ( var other in paths )
            {
                if ( other.Text == path.Text )
                {
                    throw query.Error( $"'{path.Text}' is selected twice.", start );
                }

                if ( path.Text.StartsWith( other.Text + "/", StringComparison.Ordinal ) || other.Text.StartsWith( path.Text + "/", StringComparison.Ordinal ) )
                {
                    var (shorter, longer) = path.Text.Length < other.Text.Length ? (path.Text, other.Text) : (other.Text, path.Text);

                    throw query.Error( $"'{shorter}' and '{longer}' overlap: select the object or some of its fields, not both.", start );
                }
            }

            paths.Add( path );
            start += item.Length + 1;
        }

        return paths;
    }

    /// <summary>
    /// Gets the default columns of the text table of a type, without those of the fields that the provider does not record. They
    /// are not checked as <c>--select</c> is: the user did not write them.
    /// </summary>
    public static IReadOnlyList<FieldPath> GetDefaultColumns( TypeDefinition type, IntrospectionProvider provider )
    {
        var columns = new List<FieldPath>();

        foreach ( var text in type.DefaultColumns )
        {
            var fields = new List<FieldDefinition>();
            var current = type;
            var isCount = false;
            var isProvided = true;

            foreach ( var segment in text.Split( '/' ) )
            {
                if ( segment == "$count" )
                {
                    isCount = true;

                    break;
                }

                var field = current?.GetField( segment ) ?? throw new InvalidOperationException( $"The default column '{text}' of {type.Name} is not a path of the schema." );

                if ( !provider.Provides( current.Name, field.Name ) )
                {
                    isProvided = false;

                    break;
                }

                fields.Add( field );
                current = field.Kind == ValueKind.Object ? IntrospectionSchema.GetType( field.TypeName! ) : null;
            }

            if ( isProvided )
            {
                columns.Add( new FieldPath( fields, isCount, text ) );
            }
        }

        return columns;
    }

    private static FieldPath ParsePath( QueryText query, IntrospectionUriResolver resolver, TypeDefinition type, string item, int start )
    {
        var pathText = item.Trim();
        var position = start + item.IndexOf( pathText, StringComparison.Ordinal );
        var segments = pathText.Split( '/' );
        var fields = new List<FieldDefinition>();
        var current = (TypeDefinition?) type;
        var isCount = false;

        for ( var i = 0; i < segments.Length; i++ )
        {
            var segment = segments[i];
            var prefix = string.Join( "/", segments[..i] );
            var previous = fields.Count == 0 ? null : fields[^1];

            if ( segment.Length == 0 )
            {
                throw query.Error( $"Expected a field{(i == 0 ? "" : $" after '{prefix}/'")}. {QueryText.ShellHint}", position );
            }

            if ( isCount )
            {
                throw query.Error( $"'{prefix}' is an integer, which has no field.", position );
            }

            if ( previous is { Kind: ValueKind.ObjectList or ValueKind.StringList } )
            {
                isCount = segment == "$count"
                    ? true
                    : throw query.Error( $"'{prefix}' is a collection: it is followed by $count, not by '{segment}'.", position );
            }
            else if ( current == null )
            {
                throw query.Error( $"'{prefix}' is a {previous!.Kind.ToString().ToLowerInvariant()}, which has no field.", position );
            }
            else
            {
                var field = resolver.ResolveField( current, segment, position );
                fields.Add( field );
                current = field.Kind == ValueKind.Object ? IntrospectionSchema.GetType( field.TypeName! ) : null;
            }

            position += segment.Length + 1;
        }

        return new FieldPath( fields, isCount, pathText );
    }

    private static T Parse<T>( QueryText query, TypeDefinition type, IntrospectionProvider provider, string optionName, Func<ODataQueryOptionParser, QueryCompiler, T> parse )
    {
        var model = IntrospectionEdmModel.Instance;
        var resolver = new IntrospectionUriResolver( query, provider );
        var parser = new ODataQueryOptionParser( model.Model, model.GetEdmType( type ), null, new Dictionary<string, string> { [optionName] = query.Text } )
        {
            Resolver = resolver
        };

        try
        {
            return parse( parser, new QueryCompiler( query, resolver ) );
        }
        catch ( ODataException e )
        {
            var match = PositionRegex().Match( e.Message );

            throw query.Error( e.Message, match.Success ? int.Parse( match.Groups["position"].Value, CultureInfo.InvariantCulture ) : null );
        }
    }

    private static void CheckStrippedCount( QueryText query )
    {
        // String literals are replaced by spaces of the same length, so that the positions stay.
        var text = StringLiteralRegex().Replace( query.Text, m => new string( ' ', m.Length ) );
        var match = StrippedCountRegex().Match( text );

        if ( match.Success )
        {
            var found = match.Groups["next"].Success ? $"'{match.Groups["next"].Value}'" : "nothing";

            throw query.Error( $"Expected a field, $count, any(...) or all(...) after '/' but found {found}. {QueryText.ShellHint}", match.Index + 1 );
        }
    }
}
