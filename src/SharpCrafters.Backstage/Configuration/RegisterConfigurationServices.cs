// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Application;
using SharpCrafters.Backstage.Configuration.Registry;
using SharpCrafters.Backstage.Extensibility;
using SharpCrafters.Backstage.Infrastructure;
using System;
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
    /// <para>
    /// Setting the environment variable named by <see cref="RegistryAccessDisabledVariableName"/>, with the prefix of
    /// the product, also gives the file-based manager. See that field for what it is for.
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

                    if ( schemaProvider == null || IsRegistryAccessDisabled( serviceProvider ) )
                    {
                        return fileConfigurationManager;
                    }

                    return new RegistryConfigurationManager( serviceProvider, fileConfigurationManager, schemaProvider.GetSchemas() );
                } );
    }

    /// <summary>
    /// The name, without the environment variable prefix of the product, of the variable that forbids the process to
    /// read or write the Windows registry. Setting it to a non-empty value forbids it.
    /// </summary>
    /// <remarks>
    /// It exists for a machine on which the account that builds has no access to the registry, where the alternative
    /// is a process that fails instead of keeping its settings in files. It is deliberately undocumented, because a
    /// user who set it would move their registered license keys and their license audit record out of the place the
    /// other versions and the other tools of the product read. PostSharp 2026.0 reads the same variable under the
    /// same name.
    /// </remarks>
    public const string RegistryAccessDisabledVariableName = "REGISTRY_ACCESS_DISABLED";

    /// <summary>
    /// Determines whether the environment forbids the process to access the Windows registry.
    /// </summary>
    private static bool IsRegistryAccessDisabled( IServiceProvider serviceProvider )
    {
        var productProfile = serviceProvider.GetBackstageService<ProductProfile>();

        if ( productProfile == null )
        {
            return false;
        }

        var environmentVariableProvider = serviceProvider.GetRequiredBackstageService<IEnvironmentVariableProvider>();

        // An empty value does not count as set, which is the convention of the other variables read through this
        // service, and a variable cannot be given an empty value from a Windows command line anyway.
        return !string.IsNullOrEmpty(
            environmentVariableProvider.GetEnvironmentVariable( productProfile.GetEnvironmentVariableName( RegistryAccessDisabledVariableName ) ) );
    }
}
