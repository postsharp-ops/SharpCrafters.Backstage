// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Aspects.Introspection.Query.Output;
using SharpCrafters.Aspects.Introspection.Query.Querying;
using Spectre.Console;
using System;
using System.Linq;
using System.Text.Json;

namespace SharpCrafters.Aspects.Introspection.Query.Commands;

/// <summary>
/// <c>introspect list &lt;collection&gt;</c>.
/// </summary>
public sealed class ListCommand : IntrospectionCommand<ListCommandSettings>
{
    /// <inheritdoc />
    protected override int Execute( IntrospectionCommandOptions options, ListCommandSettings settings )
    {
        var provider = options.Provider;
        var type = IntrospectionSchema.FindCollection( settings.Collection )
                   ?? throw new IntrospectionCommandException(
                       $"'{settings.Collection}' is not a collection. The collections are: {IntrospectionSchema.CollectionNames}.",
                       IntrospectionExitCodes.InvalidQuery );

        CheckProvided( provider, type );

        // The query is parsed and bound before any file is read, so that an invalid one fails fast.
        var query = new ListQuery(
            type,
            settings.Where.Select( w => QueryOptionParser.ParseCondition( w, type, provider ) ).ToList(),
            settings.Select == null ? null : QueryOptionParser.ParsePaths( settings.Select, "--select", type, provider ),
            settings.OrderBy == null ? [] : QueryOptionParser.ParseOrderings( settings.OrderBy, type, provider ),
            Math.Max( 0, settings.Skip ),
            settings.GetEffectiveLimit() );

        var session = IntrospectionSession.Open( options, settings );
        var context = session.Context;
        var (items, matching) = query.Run( context );

        // The default limit keeps the output readable by an agent; the output says when it cut the result.
        var truncated = query.Skip + items.Count < matching.Count;

        switch ( settings.Format )
        {
            case "text" when settings.Count:
                options.StandardOutput.WriteLine( matching.Count );

                break;

            case "text":
                var columns = query.Selection ?? QueryOptionParser.ParsePaths( string.Join( ",", type.DefaultColumns ), "--select", type, provider );
                options.StandardOutput.Write( TextOutput.Table( context, items, columns ) );

                if ( truncated )
                {
                    options.StandardOutput.WriteLine(
                        $"Showing {items.Count} of {matching.Count} {type.CollectionName}, from {query.Skip + 1}. Use --skip and --limit for another page, --all for all of them, or --count for their number." );
                }

                options.StandardOutput.WriteLine( TextOutput.Files( context, session.Targets ) );

                break;

            case "jsonl" when !settings.Count:
                foreach ( var item in items )
                {
                    options.StandardOutput.WriteLine( JsonOutput.Write( w => WriteItem( w, context, query, item ), indented: false ) );
                }

                break;

            default:
                WriteJson(
                    options,
                    settings,
                    JsonOutput.Write(
                        w =>
                        {
                            w.WriteStartObject();
                            JsonOutput.WriteFiles( w, context, session.Targets );

                            // The number of objects that satisfy the conditions, whatever the page.
                            w.WriteNumber( "count", matching.Count );

                            if ( !settings.Count )
                            {
                                w.WriteBoolean( "truncated", truncated );
                                w.WriteStartArray( "items" );

                                foreach ( var item in items )
                                {
                                    WriteItem( w, context, query, item );
                                }

                                w.WriteEndArray();

                                if ( truncated )
                                {
                                    JsonOutput.WriteByNamespace( w, matching );
                                }
                            }

                            w.WriteEndObject();
                        },
                        settings.Format == "json" ) );

                break;
        }

        return IntrospectionExitCodes.Success;
    }

    private static void WriteItem( Utf8JsonWriter writer, QueryContext context, ListQuery query, object item )
    {
        if ( query.Selection != null )
        {
            JsonOutput.WriteSelection( writer, context, item, query.Selection );
        }
        else
        {
            JsonOutput.WriteObject( writer, context, query.Type, item, JsonDetail.Full );
        }
    }

    internal static void CheckProvided( IntrospectionProvider provider, TypeDefinition type )
    {
        if ( !provider.Provides( type.Name, null ) )
        {
            throw new IntrospectionCommandException( $"The files of {provider.ProductName} do not record {type.CollectionName}.", IntrospectionExitCodes.InvalidQuery );
        }
    }
}
