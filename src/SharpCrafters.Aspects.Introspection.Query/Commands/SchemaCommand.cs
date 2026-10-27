// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Aspects.Introspection.Query.Output;
using SharpCrafters.Aspects.Introspection.Query.Querying;

namespace SharpCrafters.Aspects.Introspection.Query.Commands;

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
                var show = type.HasIdentifier ? $" (show {type.ItemName})" : "";
                options.StandardOutput.WriteLine( $"{type.CollectionName}{show}: {type.Description}{provided}" );

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
                        w.WriteBoolean( "showable", type.HasIdentifier );
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
