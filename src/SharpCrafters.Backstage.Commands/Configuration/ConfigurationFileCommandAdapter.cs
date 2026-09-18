// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Configuration;
using SharpCrafters.Backstage.Configuration.Registry;
using SharpCrafters.Backstage.Extensibility;
using SharpCrafters.Backstage.Serialization;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;

namespace SharpCrafters.Backstage.Commands.Configuration;

internal abstract class ConfigurationFileCommandAdapter
{
    public abstract string Alias { get; }

    public abstract string? Description { get; }

    public virtual string? EnvironmentVariableName => null;

    /// <summary>
    /// Gets the type of the configuration object, so that a caller can read and serialize it through
    /// <see cref="IConfigurationManager"/> without knowing that type at compile time.
    /// </summary>
    public abstract Type ConfigurationType { get; }

    public abstract void Print( ExtendedCommandContext context );

    public abstract void Reset( ExtendedCommandContext context );

    public abstract void Edit( ExtendedCommandContext context );

    public abstract void Validate( ExtendedCommandContext context );
}

#pragma warning disable SA1402

internal sealed class ConfigurationFileCommandAdapter<T> : ConfigurationFileCommandAdapter
    where T : ConfigurationFile, new()
{
    private readonly ConfigurationFileAttribute _attribute;

    public ConfigurationFileCommandAdapter()
    {
        this._attribute = typeof(T).GetCustomAttribute<ConfigurationFileAttribute>()
                          ?? throw new InvalidOperationException(
                              $"'{nameof(ConfigurationFileAttribute)}' custom attribute not found for '{typeof(T).FullName}' type." );

        this.Description = typeof(T).GetCustomAttribute<DescriptionAttribute>()?.Description;
    }

    public override string Alias => this._attribute.Alias;

    public override string? Description { get; }

    public override string? EnvironmentVariableName => this._attribute.EnvironmentVariableName;

    public override Type ConfigurationType => typeof(T);

    public override void Print( ExtendedCommandContext context )
    {
        var configurationManager = context.ServiceProvider.GetRequiredBackstageService<IConfigurationManager>();
        var jsonService = context.ServiceProvider.GetRequiredBackstageService<IJsonSerializationService>();

        var configuration = configurationManager.Get( typeof(T) );

        context.Console.WriteMessage( jsonService.Serialize( configuration, typeof(T) ) );
    }

    public override void Reset( ExtendedCommandContext context )
    {
        var configurationManager = context.ServiceProvider.GetRequiredBackstageService<IConfigurationManager>();

        configurationManager.Update<T>( _ => new T() );

        context.Console.WriteSuccess( $"The configuration '{this.Alias}' has been reset." );
    }

    public override void Edit( ExtendedCommandContext context )
    {
        var configurationManager = context.ServiceProvider.GetRequiredBackstageService<IConfigurationManager>();

        // The store is created with its default content first, so that the editor opens on something the user can
        // read and change rather than on nothing at all.
        configurationManager.CreateIfMissing<T>();

        var store = configurationManager.GetStore<T>();

        switch ( store.Kind )
        {
            case ConfigurationStoreKind.File:
                context.Console.WriteSuccess( $"Opening '{store.Path}' in the default editor." );
                Process.Start( new ProcessStartInfo( store.Path ) { UseShellExecute = true } );

                break;

            case ConfigurationStoreKind.RegistryKey:
                context.Console.WriteSuccess( $"Opening '{store.Path}' in the registry editor." );
                RegistryEditor.Open( store.Path );

                break;

            default:
                throw new InvalidOperationException( $"Cannot open a configuration store of kind {store.Kind}." );
        }
    }

    public override void Validate( ExtendedCommandContext context )
    {
        var configurationManager = context.ServiceProvider.GetRequiredBackstageService<IConfigurationManager>();

        // The side effect of getting the configuration is to get the warnings.
        _ = configurationManager.Get( typeof(T) );

        if ( context.Console is { HasErrors: false, HasWarnings: false } )
        {
            context.Console.WriteSuccess( $"The configuration '{configurationManager.GetStore<T>()}' is correct." );
        }
    }
}