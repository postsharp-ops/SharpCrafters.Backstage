// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Aspects.Introspection.Query.Output;
using SharpCrafters.Aspects.Introspection.Query.Querying;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SharpCrafters.Aspects.Introspection.Query.Commands;

/// <summary>
/// <c>introspect show &lt;collection&gt; &lt;selector&gt;</c> (Documentation/future/Introspection-Cli.md of PostSharp, sections
/// 5.2 and 5.5).
/// </summary>
public sealed partial class ShowCommand : IntrospectionCommand<ShowCommandSettings>
{
    // A documentation comment identifier: one letter and a colon, possibly qualified with its assembly.
    [GeneratedRegex( @"^(\[[^\]]+\])?[A-Z]:", RegexOptions.CultureInvariant )]
    private static partial Regex DocumentationIdRegex();

    // A source position: a path, a line, and optionally a column.
    [GeneratedRegex( @"^(?<path>.+?):(?<line>\d+)(:\d+)?$", RegexOptions.CultureInvariant )]
    private static partial Regex PositionRegex();

    [GeneratedRegex( "^[0-9a-f]{1,12}$", RegexOptions.CultureInvariant )]
    private static partial Regex IdentifierPrefixRegex();

    // The generic arity and the parameters of a documentation comment identifier, which a C# name omits.
    [GeneratedRegex( @"``?\d+|\(.*$|~.*$|;.*$", RegexOptions.CultureInvariant )]
    private static partial Regex NameDecorationsRegex();

    /// <inheritdoc />
    protected override int Execute( IntrospectionCommandOptions options, ShowCommandSettings settings )
    {
        var type = IntrospectionSchema.FindItem( settings.Collection )
                   ?? (IntrospectionSchema.FindCollection( settings.Collection ) is { } plural
                       ? throw new IntrospectionCommandException( $"show takes one object: write '{plural.ItemName}', not '{plural.CollectionName}'.", IntrospectionExitCodes.InvalidQuery )
                       : throw new IntrospectionCommandException(
                           $"'{settings.Collection}' is not a kind of object. show takes declaration, aspect-instance, predecessor or aspect-class.",
                           IntrospectionExitCodes.InvalidQuery ));

        if ( type.Name is not ("declaration" or "aspectInstance" or "predecessor" or "aspectClass") )
        {
            throw new IntrospectionCommandException(
                $"show is not available for {type.ItemName}, whose objects have no identifier: use 'list {type.CollectionName}' with --where.",
                IntrospectionExitCodes.InvalidQuery );
        }

        ListCommand.CheckProvided( options.Provider, type );

        var session = IntrospectionSession.Open( options, settings );
        var context = session.Context;

        switch ( type.Name )
        {
            case "declaration":
                return this.ShowDeclaration( options, settings, session );

            case "aspectInstance":
                var instance = SelectByIdentifier( context.Model.AspectInstances, i => i.Id, i => i.StructuralKey, settings.Selector, "aspect instance" );

                return Write(
                    options,
                    settings,
                    session,
                    type,
                    instance,
                    () => TextOutput.AspectInstance( context, instance ) );

            case "predecessor":
                var predecessor = SelectByIdentifier( context.Model.Predecessors, p => p.Id, p => p.StructuralKey, settings.Selector, "predecessor" );

                return Write(
                    options,
                    settings,
                    session,
                    type,
                    predecessor,
                    () => TextOutput.Predecessor( context, predecessor ) + Environment.NewLine
                                                                       + string.Concat( predecessor.AspectInstances.Select( i => Environment.NewLine + TextOutput.AspectInstance( context, i ) ) ) );

            default:
                var aspectClass = SelectAspectClass( context.Model, settings.Selector );

                return Write(
                    options,
                    settings,
                    session,
                    type,
                    aspectClass,
                    () => $"Aspect class {aspectClass.FullName} ({aspectClass.AspectInstances.Count} aspect instances)." + Environment.NewLine
                                                                                                                     + string.Concat(
                                                                                                                         aspectClass.AspectInstances.Select(
                                                                                                                             i => Environment.NewLine + TextOutput.AspectInstance( context, i ) ) ) );
        }
    }

    private static int Write( IntrospectionCommandOptions options, ShowCommandSettings settings, IntrospectionSession session, TypeDefinition type, object value, Func<string> describe )
    {
        if ( settings.Format == "text" )
        {
            options.StandardOutput.Write( describe() );
            options.StandardOutput.WriteLine();
            options.StandardOutput.WriteLine( TextOutput.Files( session.Context, session.Targets ) );
        }
        else
        {
            WriteJson(
                options,
                settings,
                JsonOutput.Write(
                    w =>
                    {
                        w.WriteStartObject();
                        JsonOutput.WriteFiles( w, session.Context, session.Targets );
                        w.WritePropertyName( type.Name );
                        JsonOutput.WriteObject( w, session.Context, type, value, JsonDetail.Expanded );
                        w.WriteEndObject();
                    },
                    settings.Format == "json" ) );
        }

        return IntrospectionExitCodes.Success;
    }

    private int ShowDeclaration( IntrospectionCommandOptions options, ShowCommandSettings settings, IntrospectionSession session )
    {
        var context = session.Context;
        var declaration = SelectDeclaration( options, context, settings.Selector );

        if ( declaration == null )
        {
            return IntrospectionExitCodes.Success;
        }

        // A declaration brings its accessors and its backing field, and, with --members, its members and theirs.
        var related = new List<IntrospectionDeclaration>();

        void Add( IntrospectionDeclaration d )
        {
            if ( related.Contains( d ) )
            {
                return;
            }

            related.Add( d );
            d.Accessors.ToList().ForEach( Add );

            if ( d.BackingField != null )
            {
                Add( d.BackingField );
            }
        }

        Add( declaration );

        if ( settings.Members )
        {
            declaration.Members.ToList().ForEach( Add );
        }

        if ( settings.Format == "text" )
        {
            options.StandardOutput.Write( TextOutput.Declaration( context, declaration, related ) );
            options.StandardOutput.WriteLine();
            options.StandardOutput.WriteLine( TextOutput.Files( context, session.Targets ) );

            return IntrospectionExitCodes.Success;
        }

        var instances = related.SelectMany( context.GetAspectInstances )
            .Concat( related.SelectMany( context.GetTransformations ).Select( t => t.AspectInstance ) )
            .Distinct()
            .ToList();

        WriteJson(
            options,
            settings,
            JsonOutput.Write(
                w =>
                {
                    w.WriteStartObject();
                    JsonOutput.WriteFiles( w, context, session.Targets );
                    w.WritePropertyName( "declaration" );
                    JsonOutput.WriteObject( w, context, IntrospectionSchema.GetType( "declaration" ), declaration, JsonDetail.Full );
                    WriteList( w, "relatedDeclarations", "declaration", related.Skip( 1 ), JsonDetail.Summary );
                    WriteList( w, "aspectInstances", "aspectInstance", instances, JsonDetail.Expanded );
                    WriteList( w, "annotations", "annotation", related.SelectMany( context.GetAnnotations ).Distinct(), JsonDetail.Full );
                    WriteList( w, "declarationMappings", "declarationMapping", related.SelectMany( context.GetDeclarationMappings ).Distinct(), JsonDetail.Full );
                    w.WriteEndObject();
                },
                settings.Format == "json" ) );

        return IntrospectionExitCodes.Success;

        void WriteList( Utf8JsonWriter w, string name, string typeName, IEnumerable<object> items, JsonDetail detail )
        {
            var itemType = IntrospectionSchema.GetType( typeName );
            w.WriteStartArray( name );

            foreach ( var item in items )
            {
                JsonOutput.WriteObject( w, context, itemType, item, detail );
            }

            w.WriteEndArray();
        }
    }

    /// <summary>
    /// Selects a declaration by a source position, an exact documentation comment identifier, or a C# name, possibly partial.
    /// Returns <c>null</c> after writing why when the position has a declaration that the file does not mention.
    /// </summary>
    internal static IntrospectionDeclaration? SelectDeclaration( IntrospectionCommandOptions options, QueryContext context, string selector )
    {
        var declarations = context.Model.Declarations;

        if ( DocumentationIdRegex().IsMatch( selector ) )
        {
            var exact = declarations.Where( d => d.Id == selector || d.QualifiedId == selector ).ToList();

            return exact.Count switch
            {
                1 => exact[0],
                0 => throw new IntrospectionCommandException(
                    $"No declaration of the file has the identifier '{selector}'. A declaration to which no aspect applies is not in the file.",
                    IntrospectionExitCodes.InvalidQuery ),
                _ => throw Ambiguous( selector, exact )
            };
        }

        var position = PositionRegex().Match( selector );

        if ( position.Success && Path.HasExtension( position.Groups["path"].Value ) )
        {
            return SelectDeclarationAtPosition( options, context, position.Groups["path"].Value, int.Parse( position.Groups["line"].Value, CultureInfo.InvariantCulture ) );
        }

        // A C# name without parameters: the whole name, or its end at a period.
        var matches = declarations.Where( d => d.Kind is not (IntrospectionDeclarationKind.Parameter or IntrospectionDeclarationKind.ReturnParameter or IntrospectionDeclarationKind.Assembly) )
            .Where(
                d =>
                {
                    var name = GetCSharpName( d );

                    return name == selector || name.EndsWith( "." + selector, StringComparison.Ordinal );
                } )
            .ToList();

        return matches.Count switch
        {
            1 => matches[0],
            0 => throw new IntrospectionCommandException(
                $"No declaration of the file is named '{selector}'. A declaration to which no aspect applies is not in the file; "
                + "'introspect list declarations' lists those that are.",
                IntrospectionExitCodes.InvalidQuery ),
            _ => throw Ambiguous( selector, matches )
        };
    }

    private static IntrospectionDeclaration? SelectDeclarationAtPosition( IntrospectionCommandOptions options, QueryContext context, string path, int line )
    {
        if ( context.Locations == null )
        {
            throw new IntrospectionCommandException(
                $"{context.Provider.ProductName} records no source location for this project, so a declaration cannot be selected by its position: give its name.",
                IntrospectionExitCodes.InvalidQuery );
        }

        var file = Path.GetFullPath( Path.Combine( options.WorkingDirectory, path ) );
        var ids = context.Locations.FindDeclarations( file, line );

        if ( ids.Count == 0 )
        {
            throw new IntrospectionCommandException( $"No declaration of '{context.Project}' is at {path}:{line}.", IntrospectionExitCodes.InvalidQuery );
        }

        // The innermost declaration that the file mentions: a member before its type.
        foreach ( var id in ids )
        {
            var declaration = context.Model.Declarations.FirstOrDefault( d => d.Id == id && d.Assembly == context.Target.AssemblyName )
                              ?? context.Model.Declarations.FirstOrDefault( d => d.Id == id );

            if ( declaration != null )
            {
                return declaration;
            }
        }

        options.StandardOutput.WriteLine( $"No aspect applies to {ids[0]} at {path}:{line}, or to the declarations that contain it." );

        return null;
    }

    private static T SelectByIdentifier<T>( IReadOnlyList<T> items, Func<T, string> getId, Func<T, string> getKey, string selector, string what )
        where T : class
    {
        var byKey = items.Where( i => getKey( i ) == selector ).ToList();

        if ( byKey.Count == 1 )
        {
            return byKey[0];
        }

        if ( !IdentifierPrefixRegex().IsMatch( selector ) )
        {
            throw new IntrospectionCommandException(
                $"'{selector}' is neither the identifier of an {what} (12 hexadecimal characters, or a unique prefix of them) nor its structural key.",
                IntrospectionExitCodes.InvalidQuery );
        }

        var matches = items.Where( i => getId( i ).StartsWith( selector, StringComparison.Ordinal ) ).ToList();

        return matches.Count switch
        {
            1 => matches[0],
            0 => throw new IntrospectionCommandException( $"No {what} has an identifier that starts with '{selector}'.", IntrospectionExitCodes.InvalidQuery ),
            _ => throw new IntrospectionCommandException(
                $"'{selector}' is the prefix of the identifiers of several {what}s:" + Environment.NewLine
                                                                                     + string.Join( Environment.NewLine, matches.Select( m => $"  {getId( m )}  {getKey( m )}" ) ),
                IntrospectionExitCodes.Ambiguous )
        };
    }

    private static IntrospectionAspectClass SelectAspectClass( IntrospectionModel model, string selector )
    {
        var matches = model.AspectClasses.Where( c => c.FullName == selector || c.ShortName == selector || c.Declaration.Name == selector ).ToList();

        if ( matches.Count == 0 )
        {
            matches = model.AspectClasses.Where( c => string.Equals( c.ShortName, selector, StringComparison.OrdinalIgnoreCase ) ).ToList();
        }

        return matches.Count switch
        {
            1 => matches[0],
            0 => throw new IntrospectionCommandException(
                $"No aspect class is named '{selector}'. The aspect classes are: {string.Join( ", ", model.AspectClasses.Select( c => c.ShortName ) )}.",
                IntrospectionExitCodes.InvalidQuery ),
            _ => throw new IntrospectionCommandException(
                $"Several aspect classes are named '{selector}': give the full name." + Environment.NewLine
                                                                                     + string.Join( Environment.NewLine, matches.Select( m => "  " + m.FullName ) ),
                IntrospectionExitCodes.Ambiguous )
        };
    }

    // The C# name of a declaration: its documentation comment identifier without the prefix, the arity and the parameters.
    internal static string GetCSharpName( IntrospectionDeclaration declaration ) => NameDecorationsRegex().Replace( declaration.Id.Substring( 2 ), "" );

    private static IntrospectionCommandException Ambiguous( string selector, IEnumerable<IntrospectionDeclaration> candidates )
        => new(
            $"'{selector}' names several declarations; give one of their identifiers, quoted:" + Environment.NewLine
                                                                                               + string.Join( Environment.NewLine, candidates.Select( c => $"  '{c.Id}'" ) ),
            IntrospectionExitCodes.Ambiguous );
}
