// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Aspects.Introspection.Query.Projects;
using SharpCrafters.Aspects.Introspection.Query.Querying;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace SharpCrafters.Aspects.Introspection.Query.Output;

/// <summary>
/// Writes the text output of the commands: a table for <c>list</c>, and sentences for <c>show</c>, which a person and an
/// agent read alike (Documentation/future/Introspection-Cli.md of PostSharp, section 5.8).
/// </summary>
internal static class TextOutput
{
    public static string Table( QueryContext context, IReadOnlyList<object> items, IReadOnlyList<FieldPath> columns )
    {
        var rows = items.Select( item => columns.Select( c => FormatCell( c.Evaluate( context, item ) ) ).ToArray() ).ToList();
        var headers = columns.Select( c => c.Text ).ToArray();
        var widths = headers.Select( ( h, i ) => Math.Max( h.Length, rows.Count == 0 ? 0 : rows.Max( r => r[i].Length ) ) ).ToArray();

        var builder = new StringBuilder();
        AppendRow( headers );
        AppendRow( widths.Select( w => new string( '-', w ) ).ToArray() );

        foreach ( var row in rows )
        {
            AppendRow( row );
        }

        return builder.ToString();

        void AppendRow( string[] cells )
        {
            for ( var i = 0; i < cells.Length; i++ )
            {
                builder.Append( i == cells.Length - 1 ? cells[i] : cells[i].PadRight( widths[i] + 2 ) );
            }

            builder.AppendLine();
        }
    }

    public static string Files( QueryContext context, IEnumerable<IntrospectionTarget> targets )
        => string.Join(
            Environment.NewLine,
            targets.Select(
                t => string.Create(
                    CultureInfo.InvariantCulture,
                    $"From {context.GetRelativePath( t.FilePath )} ({t.Project.Name}, {t.TargetFramework}, {t.Configuration}), written {t.WrittenAtUtc:yyyy-MM-dd HH:mm:ss} UTC{(t.IsStale ? "; STALE: a source file is newer, build the project" : "")}." ) ) );

    /// <summary>
    /// Describes a declaration: what applies to it, to its accessors and to its backing field, in order.
    /// </summary>
    public static string Declaration( QueryContext context, IntrospectionDeclaration declaration, IReadOnlyList<IntrospectionDeclaration> related )
    {
        var builder = new StringBuilder();
        var location = context.GetLocation( declaration );
        builder.AppendLine( CultureInfo.InvariantCulture, $"{declaration.Kind} {Name( declaration )}{(location != null ? " at " + location : "")} ({declaration.Assembly})." );

        var instances = related.SelectMany( context.GetAspectInstances ).Distinct().ToList();
        var transformations = related.SelectMany( context.GetTransformations ).Distinct().ToList();

        // The aspect instances applied to the declaration, and those whose transformations change it.
        foreach ( var instance in transformations.Select( t => t.AspectInstance ) )
        {
            if ( !instances.Contains( instance ) )
            {
                instances.Add( instance );
            }
        }

        if ( instances.Count == 0 )
        {
            builder.AppendLine( "No aspect applies to it." );
        }

        foreach ( var instance in instances )
        {
            builder.AppendLine();
            builder.Append( AspectInstance( context, instance, transformations ) );
        }

        var annotations = related.SelectMany( context.GetAnnotations ).Distinct().ToList();

        if ( annotations.Count > 0 )
        {
            builder.AppendLine();
            builder.AppendLine( "Annotations:" );

            foreach ( var annotation in annotations )
            {
                builder.AppendLine( CultureInfo.InvariantCulture, $"  - {annotation.Description ?? annotation.AnnotationClass?.Id} (on {Name( annotation.TargetDeclaration )})" );
            }
        }

        var mappings = related.SelectMany( context.GetDeclarationMappings ).Distinct().ToList();

        if ( mappings.Count > 0 )
        {
            builder.AppendLine();
            builder.AppendLine( "Declaration mappings:" );

            foreach ( var mapping in mappings )
            {
                builder.AppendLine( CultureInfo.InvariantCulture, $"  - {mapping.Source.Id} is {mapping.Kind?.ToString().ToLowerInvariant() ?? "mapped"} as {mapping.Target.Id}" );
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// Describes an aspect instance: its class, its target, its predecessors and its transformations in order. When
    /// <paramref name="transformations"/> is given, only these transformations are listed.
    /// </summary>
    public static string AspectInstance( QueryContext context, IntrospectionAspectInstance instance, IReadOnlyList<IntrospectionTransformation>? transformations = null )
    {
        var builder = new StringBuilder();
        builder.AppendLine( CultureInfo.InvariantCulture, $"Aspect {instance.AspectClass.ShortName} [{instance.Id}] on {instance.TargetDeclaration.Kind.ToString().ToLowerInvariant()} {Name( instance.TargetDeclaration )}." );

        foreach ( var predecessor in instance.Predecessors )
        {
            builder.AppendLine( "  " + Predecessor( context, predecessor ) );
        }

        var listed = instance.Transformations.Where( t => transformations == null || transformations.Contains( t ) ).ToList();

        if ( listed.Count > 0 )
        {
            builder.AppendLine( "  Transformations, in order:" );

            foreach ( var transformation in listed.OrderBy( t => t.Order ) )
            {
                var semantic = transformation.Semantic != null ? $" ({transformation.Semantic})" : "";
                builder.AppendLine( CultureInfo.InvariantCulture, $"    {transformation.Order}. {Name( transformation.TargetDeclaration )}{semantic}: {transformation.Description ?? "(no description)"}" );
            }
        }

        if ( instance.Tags != null )
        {
            builder.AppendLine( CultureInfo.InvariantCulture, $"  Tags: {string.Join( ", ", QueryContext.SplitTags( instance.Tags ) )}." );
        }

        return builder.ToString();
    }

    /// <summary>
    /// Describes a predecessor in one sentence.
    /// </summary>
    public static string Predecessor( QueryContext context, IntrospectionPredecessor predecessor )
    {
        var location = context.GetLocation( predecessor );
        var at = location != null ? $" at {location}" : "";
        var inferred = predecessor.IsKindInferred ? " (kind inferred)" : "";

        return predecessor.Kind switch
        {
            AspectPredecessorKind.Attribute => $"Created by the attribute [{predecessor.Id}] on {Name( predecessor.Declaration )}{at}{inferred}.",
            AspectPredecessorKind.Multicast => $"Created by the multicast attribute [{predecessor.Id}] on {Name( predecessor.Declaration )}{at}{inferred}.",
            AspectPredecessorKind.Inherited => $"Created by the inherited attribute [{predecessor.Id}] on {Name( predecessor.Declaration )}{at}{inferred}.",
            AspectPredecessorKind.Xml => $"Created by the XML element [{predecessor.Id}]{at}{inferred}.",
            AspectPredecessorKind.ChildAspect when predecessor.AspectInstance != null =>
                $"Provided by the aspect {predecessor.AspectInstance.AspectClass.ShortName} [{predecessor.AspectInstance.Id}] on {Name( predecessor.AspectInstance.TargetDeclaration )}{inferred}.",
            _ => $"Created by a predecessor of kind {predecessor.Kind} [{predecessor.Id}]{at}{inferred}."
        };
    }

    // A documentation comment identifier without its prefix, which reads as the C# name with its parameters.
    public static string Name( IntrospectionDeclaration? declaration ) => declaration == null ? "(unknown)" : declaration.Id.Length > 2 ? declaration.Id.Substring( 2 ) : declaration.Id;

    private static string FormatCell( object? value )
        => value switch
        {
            null => "",
            string s => s,
            bool b => b ? "true" : "false",
            long l => l.ToString( CultureInfo.InvariantCulture ),
            IntrospectionDeclaration d => d.Id,
            IntrospectionAspectClass c => c.ShortName,
            IntrospectionAspectInstance i => i.Id,
            IntrospectionPredecessor p => p.Id,
            IEnumerable list => string.Join( ", ", list.Cast<object>().Select( FormatCell ) ),
            _ => value.ToString() ?? ""
        };
}
