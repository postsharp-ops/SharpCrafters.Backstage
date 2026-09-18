// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Configuration.Registry;
using SharpCrafters.Backstage.Extensibility;

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
    /// Registers an <see cref="IConfigurationManager"/> that keeps the configuration objects a product shares with
    /// its earlier versions in the Windows registry, and the rest in files. It requires the core services and the
    /// Windows registry service.
    /// </summary>
    /// <param name="serviceProviderBuilder">The builder.</param>
    /// <param name="schemas">
    /// Which configuration objects live in the registry, and where. They belong to the product, whose earlier
    /// versions chose the names.
    /// </param>
    public static ServiceProviderBuilder AddRegistryConfigurationServices(
        this ServiceProviderBuilder serviceProviderBuilder,
        params IRegistryConfigurationSchema[] schemas )
        => serviceProviderBuilder
            .AddSingleton<IRegistryService>( _ => WindowsRegistryService.Instance )
            .AddSingleton<IConfigurationManager>(
                serviceProvider => new RegistryConfigurationManager( serviceProvider, new ConfigurationManager( serviceProvider ), schemas ) );
}
