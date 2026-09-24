// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using JetBrains.Annotations;
using SharpCrafters.Backstage.Diagnostics;
using SharpCrafters.Backstage.Infrastructure;
using SharpCrafters.Backstage.Licensing.Consumption;
using SharpCrafters.Backstage.Licensing.Consumption.Sources;
using System;

namespace SharpCrafters.Backstage.Extensibility;

/// <summary>
/// Creates the service providers of the Backstage services.
/// </summary>
/// <remarks>
/// <para>
/// An application creates its provider with <see cref="CreateServiceProvider"/> and passes it to whatever needs a
/// service. An application that has a container of its own registers the services in it through
/// <see cref="ServiceProviderBuilder"/>, then calls <see cref="InitializeBackstageServices"/> on the provider it built.
/// </para>
/// <para>
/// The process-wide provider (<see cref="ServiceProvider"/>, <see cref="IsInitialized"/> and <see cref="Initialize"/>)
/// is obsolete. A static provider makes the result of a call depend on which code initialized the process first. It
/// also prevents two providers from coexisting in one process, so tests of different configurations cannot run in
/// parallel.
/// </para>
/// </remarks>
[PublicAPI]
public static class BackstageServiceFactory
{
    internal const string GlobalProviderObsoleteMessage =
        "The process-wide service provider is obsolete. Create a provider with CreateServiceProvider, or register the services in the "
        + "container of the application through a ServiceProviderBuilder, and pass the provider explicitly.";

    private static readonly object _initializeSync = new();

    private static IServiceProvider? _serviceProvider;

    [Obsolete( GlobalProviderObsoleteMessage )]
    public static IServiceProvider ServiceProvider
        => _serviceProvider ?? throw new InvalidOperationException( "BackstageServiceFactory.Initialize method has not been called." );

    [Obsolete( GlobalProviderObsoleteMessage )]
    public static bool IsInitialized => _serviceProvider != null;

    [Obsolete( GlobalProviderObsoleteMessage )]
    public static bool Initialize( BackstageInitializationOptions options, string caller )
    {
        lock ( _initializeSync )
        {
            if ( _serviceProvider != null )
            {
                _serviceProvider.GetLoggerFactory()
                    .GetLogger( "BackstageServiceFactory" )
                    .Trace?.Log( $"Support services initialization requested from {caller}. The services are already initialized." );

                return false;
            }

            _serviceProvider = CreateServiceProvider( options );

            _serviceProvider.GetLoggerFactory()
                .GetLogger( "BackstageServiceFactory" )
                .Trace?.Log( $"Support services initialized upon a request from {caller}." );

            return true;
        }
    }

    /// <summary>
    /// Runs the initialization that the Backstage services need once their provider exists: it starts the
    /// background services and registers the shutdown hooks. An application that builds the provider itself, from
    /// its own container, calls it once on that provider.
    /// </summary>
    /// <returns>The same provider.</returns>
    public static IServiceProvider InitializeBackstageServices( this IServiceProvider serviceProvider )
    {
        serviceProvider.GetRequiredBackstageService<BackstageServicesInitializer>().Initialize();
        serviceProvider.GetBackstageService<ShutdownService>()?.Initialize();

        return serviceProvider;
    }

    /// <summary>
    /// Creates and initializes an independent service provider. The provider is not shared with
    /// <see cref="ServiceProvider"/>, and several providers can coexist in a process. The returned object implements
    /// <see cref="IDisposable"/>; disposing it disposes the services that it created, releases the process-wide hooks
    /// that they registered, and stops their background work from being drained at process exit.
    /// </summary>
    public static IServiceProvider CreateServiceProvider( BackstageInitializationOptions options )
    {
        var serviceProviderBuilder = new SimpleServiceProviderBuilder();
        serviceProviderBuilder.AddBackstageServices( options );

        var serviceProvider = serviceProviderBuilder.ServiceProvider;
        serviceProvider.InitializeBackstageServices();

        return serviceProvider;
    }

    public static ILicenseConsumptionService CreateTestLicenseConsumptionService( IServiceProvider serviceProvider, string? licenseKey )
    {
        var sources = licenseKey == null
            ? Array.Empty<ExplicitLicenseSource>()
            : new[] { new ExplicitLicenseSource( licenseKey, LicenseSourceKind.UserProfile, serviceProvider ) };

        var service = new LicenseConsumptionService( serviceProvider, sources );

        return service;
    }
}