// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using JetBrains.Annotations;
using SharpCrafters.Backstage.Extensibility;
using System;

namespace SharpCrafters.Backstage.Maintenance;

/// <summary>
/// Registers the implementations of <see cref="IProcessShutdownStrategy"/>.
/// </summary>
/// <remarks>
/// Each method adds a registration and replaces none, so that several strategies contribute to the same command. See
/// <see cref="IProcessShutdownStrategy"/> for what this requires of the service provider.
/// </remarks>
[PublicAPI]
public static class ProcessShutdownServiceExtensions
{
    /// <summary>
    /// Registers a strategy.
    /// </summary>
    public static ServiceProviderBuilder AddProcessShutdownStrategy(
        this ServiceProviderBuilder serviceProviderBuilder,
        Func<IServiceProvider, IProcessShutdownStrategy> createStrategy )
    {
        serviceProviderBuilder.AddService( typeof(IProcessShutdownStrategy), createStrategy );

        return serviceProviderBuilder;
    }

    /// <summary>
    /// Registers the strategy that stops the MSBuild nodes and the compiler server. The commands register it for every
    /// product.
    /// </summary>
    public static ServiceProviderBuilder AddBuildServerShutdownStrategy( this ServiceProviderBuilder serviceProviderBuilder )
        => serviceProviderBuilder.AddProcessShutdownStrategy( serviceProvider => new BuildServerShutdownStrategy( serviceProvider ) );

    /// <summary>
    /// Registers the strategy that ends the worker and the notifier of the product. The commands register it for every
    /// product.
    /// </summary>
    public static ServiceProviderBuilder AddBackstageToolsShutdownStrategy( this ServiceProviderBuilder serviceProviderBuilder )
        => serviceProviderBuilder.AddProcessShutdownStrategy( serviceProvider => new BackstageToolsShutdownStrategy( serviceProvider ) );

    /// <summary>
    /// Registers the strategy that reports the processes of integrated development environments that load the Roslyn
    /// analyzers of the product. A product whose analyzers they load registers it.
    /// </summary>
    public static ServiceProviderBuilder AddDevelopmentEnvironmentShutdownStrategy( this ServiceProviderBuilder serviceProviderBuilder )
        => serviceProviderBuilder.AddProcessShutdownStrategy( serviceProvider => new DevelopmentEnvironmentShutdownStrategy( serviceProvider ) );
}
