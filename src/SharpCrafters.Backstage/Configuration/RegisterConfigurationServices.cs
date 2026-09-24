// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Configuration.Registry;
using SharpCrafters.Backstage.Extensibility;
using System.Runtime.InteropServices;

namespace SharpCrafters.Backstage.Configuration;

/// <summary>
/// Extension methods that register the configuration services in a <see cref="ServiceProviderBuilder"/>.
/// </summary>
public static class RegisterConfigurationServices
{
    /// <summary>
    /// Registers the file-based <see cref="IConfigurationManager"/>. It requires the core services.
    /// </summary>
    public static ServiceProviderBuilder AddConfigurationServices( this ServiceProviderBuilder serviceProviderBuilder )
        => serviceProviderBuilder.AddSingleton<IConfigurationManager>( serviceProvider => new ConfigurationManager( serviceProvider ) );

    /// <summary>
    /// Registers an <see cref="IConfigurationManager"/> that keeps in the Windows registry whichever configuration
    /// objects an <see cref="IRegistryConfigurationSchemaProvider"/> names, and the rest in files.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Which objects those are is the product's to say, and it says it by registering that service; the schemas
    /// belong to it, because its earlier versions chose the names. A product that registers none gets the file-based
    /// manager, and so does every product away from Windows.
    /// </para>
    /// <para>
    /// The registry service is registered on Windows only. Off it there is no registry to share anything through, so
    /// nothing of that layer is constructed or reachable rather than being constructed and then found to have nothing
    /// to read.
    /// </para>
    /// </remarks>
    public static ServiceProviderBuilder AddRegistryConfigurationServices( this ServiceProviderBuilder serviceProviderBuilder )
    {
        if ( !RuntimeInformation.IsOSPlatform( OSPlatform.Windows ) )
        {
            return serviceProviderBuilder.AddConfigurationServices();
        }

        return serviceProviderBuilder
            .AddSingleton<IRegistryService>( _ => WindowsRegistryService.Instance )
            .AddSingleton<IConfigurationManager>(
                serviceProvider =>
                {
                    var fileConfigurationManager = new ConfigurationManager( serviceProvider );
                    var schemaProvider = serviceProvider.GetBackstageService<IRegistryConfigurationSchemaProvider>();

                    if ( schemaProvider == null )
                    {
                        return fileConfigurationManager;
                    }

                    return new RegistryConfigurationManager( serviceProvider, fileConfigurationManager, schemaProvider.GetSchemas() );
                } );
    }
}
