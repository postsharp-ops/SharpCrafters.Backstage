// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Configuration;
using SharpCrafters.Backstage.Extensibility;
using SharpCrafters.Backstage.Serialization;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace SharpCrafters.Backstage.Commands.Configuration;

/// <summary>
/// Writes every configuration, or one of them, to the console as a single JSON document.
/// </summary>
/// <remarks>
/// <para>
/// The values are read through <see cref="IConfigurationManager"/> and not from the stores directly, so what is
/// printed is what the product itself sees. Each configuration is reported with the store it came from, which is what
/// makes the dump comparable with the store read by other means: on Windows, several of these configurations live in
/// the registry key that PostSharp 2026.0 shares, and exporting that key and comparing it with this dump shows whether
/// the two versions agree about what is stored there.
/// </para>
/// <para>
/// The output goes to the console as one document rather than as a table, so that it can be redirected to a file and
/// given to anything that reads JSON. <c>config print</c> remains the way to look at one configuration on its own.
/// </para>
/// </remarks>
internal sealed class DumpConfigurationCommand : BaseCommand<DumpConfigurationCommandSettings>
{
    protected override void Execute( ExtendedCommandContext context, DumpConfigurationCommandSettings settings )
    {
        var adapters = context.BackstageCommandOptions.ConfigurationsToDump;

        IEnumerable<ConfigurationFileCommandAdapter> selected;

        if ( settings.Alias == null )
        {
            selected = adapters.Values;
        }
        else if ( settings.All )
        {
            throw new CommandException( "Give either an alias or --all, not both." );
        }
        else if ( adapters.TryGetValue( settings.Alias, out var adapter ) )
        {
            selected = [adapter];
        }
        else
        {
            throw new CommandException(
                $"Invalid configuration alias: '{settings.Alias}'. The following configurations are available: {string.Join( ", ", adapters.Keys.OrderBy( k => k ) )}" );
        }

        var configurationManager = context.ServiceProvider.GetRequiredBackstageService<IConfigurationManager>();
        var jsonService = context.ServiceProvider.GetRequiredBackstageService<IJsonSerializationService>();

        using var buffer = new MemoryStream();

        using ( var writer = new Utf8JsonWriter( buffer, new JsonWriterOptions { Indented = true } ) )
        {
            writer.WriteStartObject();
            writer.WriteString( "product", context.BackstageCommandOptions.ProductProfile.Name );
            writer.WriteStartArray( "configurations" );

            foreach ( var adapter in selected.OrderBy( a => a.Alias, StringComparer.Ordinal ) )
            {
                writer.WriteStartObject();
                writer.WriteString( "alias", adapter.Alias );
                writer.WriteString( "type", adapter.ConfigurationType.FullName );

                var store = configurationManager.GetStore( adapter.ConfigurationType );
                writer.WriteStartObject( "store" );
                writer.WriteString( "kind", store.Kind.ToString() );
                writer.WriteString( "path", store.Path );
                writer.WriteEndObject();

                var configuration = configurationManager.Get( adapter.ConfigurationType );

                // Written as it was serialized rather than re-encoded, so that the dump of one configuration is
                // exactly what 'config print' shows for it.
                writer.WritePropertyName( "value" );
                writer.WriteRawValue( jsonService.Serialize( configuration, adapter.ConfigurationType ) );

                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        context.Console.WriteMessage( Encoding.UTF8.GetString( buffer.ToArray() ) );
    }
}
