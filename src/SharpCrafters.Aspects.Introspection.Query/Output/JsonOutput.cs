// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Aspects.Introspection.Query.Projects;
using SharpCrafters.Aspects.Introspection.Query.Querying;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace SharpCrafters.Aspects.Introspection.Query.Output;

/// <summary>
/// Writes the JSON output of the commands, whose shape the schema describes. Absent values are omitted rather than written as
/// <c>null</c>. Objects inside other objects are written with their summary fields, so that the output has no cycle.
/// </summary>
internal static class JsonOutput
{
    private static readonly JsonWriterOptions _indented = new() { Indented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    private static readonly JsonWriterOptions _compact = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static string Write( Action<Utf8JsonWriter> write, bool indented = true )
    {
        using var stream = new MemoryStream();

        using ( var writer = new Utf8JsonWriter( stream, indented ? _indented : _compact ) )
        {
            write( writer );
        }

        return Encoding.UTF8.GetString( stream.ToArray() );
    }

    public static void WriteFiles( Utf8JsonWriter writer, QueryContext context, IEnumerable<IntrospectionTarget> targets )
    {
        writer.WriteStartArray( "files" );

        foreach ( var target in targets )
        {
            writer.WriteStartObject();
            writer.WriteString( "project", target.Project.Name );
            writer.WriteString( "targetFramework", target.TargetFramework );
            writer.WriteString( "configuration", target.Configuration );
            writer.WriteString( "path", context.GetRelativePath( target.FilePath ) );

            if ( target.WrittenAtUtc != null )
            {
                writer.WriteString( "writtenAt", target.WrittenAtUtc.Value.ToString( "yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture ) );
            }

            writer.WriteBoolean( "stale", target.IsStale );
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    /// <param name="lists">How show writes the lists of objects, or <c>null</c> to write them as arrays.</param>
    public static void WriteObject(
        Utf8JsonWriter writer,
        QueryContext context,
        TypeDefinition type,
        object value,
        JsonDetail detail,
        int depth = 0,
        RelatedListOptions? lists = null )
    {
        writer.WriteStartObject();

        foreach ( var field in type.Fields )
        {
            if ( (detail == JsonDetail.Summary && !field.IsInSummary) || (detail == JsonDetail.Full && field.Kind == ValueKind.ObjectList)
                                                                      || !context.Provider.Provides( type.Name, field.Name ) )
            {
                continue;
            }

            WriteField( writer, context, field, field.GetValue( context, value ), detail, depth, owner: (type.Name, value), lists: lists );
        }

        writer.WriteEndObject();
    }

    /// <summary>
    /// Writes a list of objects as <c>show</c> does: <c>{ "count", "truncated", "items" }</c>, with <c>byNamespace</c> and
    /// <c>next</c> when the list has more objects than <see cref="RelatedListOptions.Limit"/>.
    /// </summary>
    /// <param name="next">The command, without <c>introspect</c>, that lists all the objects, or <c>null</c>.</param>
    public static void WriteRelatedList(
        Utf8JsonWriter writer,
        QueryContext context,
        string name,
        TypeDefinition itemType,
        IReadOnlyList<object> items,
        JsonDetail itemDetail,
        RelatedListOptions lists,
        string? next,
        int depth = 1 )
    {
        var truncated = lists.Limit is { } limit && items.Count > limit;

        writer.WriteStartObject( name );
        writer.WriteNumber( "count", items.Count );
        writer.WriteBoolean( "truncated", truncated );
        writer.WriteStartArray( "items" );

        foreach ( var item in truncated ? items.Take( lists.Limit!.Value ) : items )
        {
            WriteObject( writer, context, itemType, item, itemDetail, depth, lists );
        }

        writer.WriteEndArray();

        if ( truncated )
        {
            WriteByNamespace( writer, items );

            if ( next != null )
            {
                writer.WriteString( "next", next );
            }
        }

        writer.WriteEndObject();
    }

    /// <summary>
    /// Writes the number of objects of each namespace, the largest first.
    /// </summary>
    public static void WriteByNamespace( Utf8JsonWriter writer, IEnumerable<object> items )
    {
        writer.WriteStartArray( "byNamespace" );

        foreach ( var (ns, count) in RelatedLists.ByNamespace( items ) )
        {
            writer.WriteStartObject();
            writer.WriteString( "namespace", ns );
            writer.WriteNumber( "count", count );
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    /// <summary>
    /// Writes the paths of <c>--select</c>, nested as their fields: <c>aspectClass/shortName</c> is written as
    /// <c>"aspectClass": { "shortName": ... }</c>, and a path that ends with <c>$count</c> as a field named after it.
    /// </summary>
    public static void WriteSelection( Utf8JsonWriter writer, QueryContext context, object value, IReadOnlyList<FieldPath> selection )
    {
        var root = new Node();

        foreach ( var path in selection )
        {
            var node = root;
            var names = path.Fields.Select( f => f.Name ).ToList();

            if ( path.IsCount )
            {
                names[^1] += "/$count";
            }

            for ( var i = 0; i < names.Count - 1; i++ )
            {
                node = node.Children.GetValueOrDefault( names[i] ) ?? (node.Children[names[i]] = new Node());
            }

            node.Children[names[^1]] = new Node { Path = path };
        }

        WriteNode( root );

        void WriteNode( Node node )
        {
            writer.WriteStartObject();

            foreach ( var (name, child) in node.Children )
            {
                if ( child.Path != null )
                {
                    var path = child.Path;
                    var fieldValue = path.Evaluate( context, value );

                    if ( path.IsCount )
                    {
                        writer.WriteNumber( name, (long) fieldValue! );
                    }
                    else
                    {
                        WriteField( writer, context, path.Fields[^1], fieldValue, JsonDetail.Full, 0, name );
                    }
                }
                else
                {
                    writer.WritePropertyName( name );
                    WriteNode( child );
                }
            }

            writer.WriteEndObject();
        }
    }

    private static void WriteField(
        Utf8JsonWriter writer,
        QueryContext context,
        FieldDefinition field,
        object? value,
        JsonDetail detail,
        int depth,
        string? name = null,
        (string Type, object Value)? owner = null,
        RelatedListOptions? lists = null )
    {
        name ??= field.Name;

        switch ( value )
        {
            case null:
            case string { Length: 0 }:
                return;

            case string s:
                writer.WriteString( name, s );

                return;

            case int i:
                writer.WriteNumber( name, i );

                return;

            case long l:
                writer.WriteNumber( name, l );

                return;

            case bool b:
                writer.WriteBoolean( name, b );

                return;
        }

        switch ( field.Kind )
        {
            case ValueKind.StringList:
                writer.WriteStartArray( name );

                foreach ( var item in (IEnumerable) value )
                {
                    writer.WriteStringValue( (string) item );
                }

                writer.WriteEndArray();

                return;

            case ValueKind.Object:
                writer.WritePropertyName( name );
                WriteObject( writer, context, IntrospectionSchema.GetType( field.TypeName! ), value, JsonDetail.Summary, depth + 1 );

                return;

            case ValueKind.ObjectList:
                var itemType = IntrospectionSchema.GetType( field.TypeName! );

                // Expanded once: the objects of a list are summary rows, or, for the aspect instances of show declaration, written with
                // their fields but their own lists.
                var itemDetail = detail == JsonDetail.ExpandedFull && depth == 0 ? JsonDetail.Full : JsonDetail.Summary;

                if ( lists != null )
                {
                    var next = owner is { } o ? RelatedLists.GetNext( o.Type, field.Name, o.Value ) : null;
                    WriteRelatedList( writer, context, name, itemType, ((IEnumerable) value).Cast<object>().ToList(), itemDetail, lists, next, depth + 1 );

                    return;
                }

                writer.WriteStartArray( name );

                foreach ( var item in (IEnumerable) value )
                {
                    WriteObject( writer, context, itemType, item, itemDetail, depth + 1 );
                }

                writer.WriteEndArray();

                return;
        }
    }

    private sealed class Node
    {
        public Dictionary<string, Node> Children { get; } = new( StringComparer.Ordinal );

        public FieldPath? Path { get; init; }
    }
}
