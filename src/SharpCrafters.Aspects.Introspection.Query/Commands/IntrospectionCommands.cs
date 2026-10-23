// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Aspects.Introspection.Query.Output;
using SharpCrafters.Aspects.Introspection.Query.Querying;
using Spectre.Console;
using Spectre.Console.Cli;
using Spectre.Console.Json;
using System;
using System.Linq;
using System.Text.Json;
using System.Threading;

namespace SharpCrafters.Aspects.Introspection.Query.Commands;

/// <summary>
/// Registers the <c>introspect</c> branch in the command line of a tool, with the commands of both products and those that the
/// provider of the product adds (Documentation/future/Introspection-Cli.md of PostSharp, section 5.1). The tool calls it from
/// the <c>configureMoreCommands</c> callback of <c>BackstageCommandFactory.ConfigureCommandApp</c>.
/// </summary>
public static class IntrospectionCommandFactory
{
    /// <summary>
    /// Adds the <c>introspect</c> branch.
    /// </summary>
    public static void AddIntrospectBranch( IConfigurator configurator, IntrospectionCommandOptions options )
        => configurator.AddBranch(
            "introspect",
            branch =>
            {
                var product = options.Provider.ProductName;
                branch.SetDescription( $"Queries how {product} applied aspects to a project, from the files of its last build. Never builds." );

                branch.AddCommand<ListCommand>( "list" )
                    .WithData( options )
                    .WithDescription( "Lists the objects of a collection that satisfy conditions." )
                    .WithExample( "introspect", "list", "aspect-instances", "--where", "aspectClass/shortName eq 'Log'" );

                branch.AddCommand<ShowCommand>( "show" )
                    .WithData( options )
                    .WithDescription( "Shows one object with what relates to it, such as a declaration with its aspects and their transformations in order." )
                    .WithExample( "introspect", "show", "declaration", "src/Customer.cs:18" );

                branch.AddCommand<SchemaCommand>( "schema" )
                    .WithData( options )
                    .WithDescription( "Describes the collections and their fields, which --where, --select and --order-by use." );

                options.Provider.AddCommands( branch, options );
            } );
}

/// <summary>
/// The base of the <c>introspect</c> commands: reports an <see cref="IntrospectionCommandException"/> as an error with its exit
/// code.
/// </summary>
/// <typeparam name="T">The settings.</typeparam>
public abstract class IntrospectionCommand<T> : Command<T>
    where T : IntrospectionCommandSettings
{
    /// <inheritdoc />
    protected sealed override int Execute( CommandContext context, T settings, CancellationToken cancellationToken )
    {
        var options = (IntrospectionCommandOptions) context.Data!;

        try
        {
            return this.Execute( options, settings );
        }
        catch ( IntrospectionCommandException e )
        {
            options.ErrorOutput.WriteLine( "error: " + e.Message );

            return e.ExitCode;
        }
    }

    /// <summary>
    /// Runs the command.
    /// </summary>
    protected abstract int Execute( IntrospectionCommandOptions options, T settings );

    /// <summary>
    /// Writes a JSON document, colored with the JSON widget of Spectre.Console when the output is a terminal, unless
    /// <c>NO_COLOR</c> is set or <c>--color</c> says otherwise. The JSON never goes through markup, which reads brackets as styles.
    /// </summary>
    internal static void WriteJson( IntrospectionCommandOptions options, IntrospectionCommandSettings settings, string json )
    {
        var color = settings.Color switch
        {
            "always" => true,
            "never" => false,
            _ => options.IsOutputTerminal && string.IsNullOrEmpty( options.GetEnvironmentVariable( "NO_COLOR" ) )
        };

        if ( color && settings.Format == "json" )
        {
            var console = AnsiConsole.Create(
                new AnsiConsoleSettings
                {
                    Out = new AnsiConsoleOutput( options.StandardOutput ),
                    Ansi = AnsiSupport.Yes,

                    // Detection finds no color on a redirected output, which --color always colors.
                    ColorSystem = ColorSystemSupport.Standard,
                    Interactive = InteractionSupport.No
                } );

            // A line of JSON is never wrapped: a break inside a string would make the copied output invalid.
            console.Profile.Width = int.MaxValue / 2;
            console.Write( new JsonText( json ) );
            console.WriteLine();
        }
        else
        {
            options.StandardOutput.WriteLine( json );
        }
    }
}

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
            settings.Where.Select( w => QueryParser.ParseCondition( w, type, provider ) ).ToList(),
            settings.Select == null ? null : QueryParser.ParsePaths( settings.Select, "--select", type, provider ),
            settings.OrderBy == null ? [] : QueryParser.ParseOrderings( settings.OrderBy, type, provider ),
            Math.Max( 0, settings.Skip ),
            settings.Limit );

        var session = IntrospectionSession.Open( options, settings );
        var context = session.Context;
        var items = query.Run( context );

        switch ( settings.Format )
        {
            case "text" when settings.Count:
                options.StandardOutput.WriteLine( items.Count );

                break;

            case "text":
                var columns = query.Selection ?? QueryParser.ParsePaths( string.Join( ",", type.DefaultColumns ), "--select", type, provider );
                options.StandardOutput.Write( TextOutput.Table( context, items, columns ) );
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
                            w.WriteNumber( "count", items.Count );

                            if ( !settings.Count )
                            {
                                w.WriteStartArray( "items" );

                                foreach ( var item in items )
                                {
                                    WriteItem( w, context, query, item );
                                }

                                w.WriteEndArray();
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

/// <summary>
/// <c>introspect schema [&lt;collection&gt;]</c>.
/// </summary>
public sealed class SchemaCommand : IntrospectionCommand<SchemaCommandSettings>
{
    /// <inheritdoc />
    protected override int Execute( IntrospectionCommandOptions options, SchemaCommandSettings settings )
    {
        var provider = options.Provider;

        var types = settings.Collection == null
            ? IntrospectionSchema.Types
            : [IntrospectionSchema.FindCollection( settings.Collection ) ?? IntrospectionSchema.FindItem( settings.Collection )
               ?? throw new IntrospectionCommandException(
                   $"'{settings.Collection}' is not a collection. The collections are: {IntrospectionSchema.CollectionNames}.",
                   IntrospectionExitCodes.InvalidQuery )];

        if ( settings.Format == "text" )
        {
            foreach ( var type in types )
            {
                var provided = provider.Provides( type.Name, null ) ? "" : $" Not recorded by {provider.ProductName}.";
                options.StandardOutput.WriteLine( $"{type.CollectionName} (show {type.ItemName}): {type.Description}{provided}" );

                foreach ( var field in type.Fields )
                {
                    var fieldProvided = provider.Provides( type.Name, field.Name ) ? "" : $" Not recorded by {provider.ProductName}.";
                    var values = field.EnumValues != null ? $" Values: {string.Join( ", ", field.EnumValues )}." : "";
                    options.StandardOutput.WriteLine( $"  {field.Name} ({Describe( field )}): {field.Description}{values}{fieldProvided}" );
                }

                options.StandardOutput.WriteLine();
            }

            return IntrospectionExitCodes.Success;
        }

        WriteJson(
            options,
            settings,
            JsonOutput.Write(
                w =>
                {
                    w.WriteStartObject();
                    w.WriteString( "product", provider.ProductName );
                    w.WriteStartArray( "collections" );

                    foreach ( var type in types )
                    {
                        w.WriteStartObject();
                        w.WriteString( "name", type.CollectionName );
                        w.WriteString( "item", type.ItemName );
                        w.WriteString( "type", type.Name );
                        w.WriteString( "description", type.Description );
                        w.WriteBoolean( "provided", provider.Provides( type.Name, null ) );
                        w.WriteStartArray( "fields" );

                        foreach ( var field in type.Fields )
                        {
                            w.WriteStartObject();
                            w.WriteString( "name", field.Name );
                            w.WriteString( "type", Describe( field ) );
                            w.WriteString( "description", field.Description );
                            w.WriteBoolean( "provided", provider.Provides( type.Name, field.Name ) );
                            w.WriteBoolean( "summary", field.IsInSummary );

                            if ( field.EnumValues != null )
                            {
                                w.WriteStartArray( "values" );

                                foreach ( var value in field.EnumValues )
                                {
                                    w.WriteStringValue( value );
                                }

                                w.WriteEndArray();
                            }

                            w.WriteEndObject();
                        }

                        w.WriteEndArray();
                        w.WriteEndObject();
                    }

                    w.WriteEndArray();
                    w.WriteEndObject();
                },
                settings.Format == "json" ) );

        return IntrospectionExitCodes.Success;
    }

    private static string Describe( FieldDefinition field )
        => field.Kind switch
        {
            ValueKind.Object => field.TypeName!,
            ValueKind.ObjectList => field.TypeName + "[]",
            ValueKind.StringList => "string[]",
            ValueKind.Enum => "enum",
            _ => field.Kind.ToString().ToLowerInvariant()
        };
}
