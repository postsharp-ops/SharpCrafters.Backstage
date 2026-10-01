// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Configuration;
using SharpCrafters.Backstage.Configuration.Registry;
using SharpCrafters.Backstage.Extensibility;
using SharpCrafters.Backstage.Repositories;
using System.Runtime.InteropServices;

namespace SharpCrafters.Backstage.Telemetry;

/// <summary>
/// Extension methods that register the telemetry services in a <see cref="ServiceProviderBuilder"/>.
/// </summary>
public static class RegisterTelemetryServices
{
    /// <summary>
    /// Registers the telemetry services: usage sessions, exception reports, the upload queue and the uploaders. It
    /// requires the core and configuration services.
    /// </summary>
    public static ServiceProviderBuilder AddTelemetryServices( this ServiceProviderBuilder serviceProviderBuilder, TelemetryInitializationOptions options )
        => serviceProviderBuilder
            .AddSingleton( options )
            .AddSingleton<IRepositoryConfigurationService>( serviceProvider => new RepositoryConfigurationService( serviceProvider ) )
            .AddSingleton( serviceProvider => new TelemetryLogger( serviceProvider ) )
            .AddSingleton( serviceProvider => new ExceptionSensitiveDataHelper( serviceProvider ) )
            .AddSingleton<LocalExceptionReporter>( serviceProvider => new LocalExceptionReporter( serviceProvider ) )

            // A single ExceptionReporter instance is exposed under both IExceptionReportManager (review/upload, used by
            // the worker and CLI) and IExceptionCapturer (capture, used by the telemetry context). See #1701.
            .AddSingleton<ExceptionReporter>( serviceProvider => new ExceptionReporter( new TelemetryQueue( serviceProvider ), serviceProvider ) )
            .AddSingleton<IExceptionReportManager>( serviceProvider => serviceProvider.GetRequiredBackstageService<ExceptionReporter>() )
            .AddSingleton<IExceptionCapturer>( serviceProvider => serviceProvider.GetRequiredBackstageService<ExceptionReporter>() )
            .AddSingleton<ITelemetryUploader>( serviceProvider => new TelemetryUploader( serviceProvider ) )
            .AddSingleton<IUsageSessionFactory>( serviceProvider => new UsageSessionFactory( serviceProvider ) )
            .AddSingleton<ITelemetryConfigurationService>( serviceProvider => new TelemetryConfigurationService( serviceProvider ) )
            .AddSingleton<ITelemetryService>( serviceProvider => new TelemetryService( serviceProvider ) )
            .AddSingleton<TelemetryReportUploader>( serviceProvider => new TelemetryReportUploader( serviceProvider ) )
            .AddSingleton<MatomoUploader>( serviceProvider => new MatomoUploader( serviceProvider ) );

    /// <summary>
    /// Registers a <see cref="FileUsageSessionStore"/>, which keeps the record of each project in a file of its own.
    /// </summary>
    public static ServiceProviderBuilder AddFileUsageSessionStore( this ServiceProviderBuilder serviceProviderBuilder )
        => serviceProviderBuilder.AddSingleton<IUsageSessionStore>( serviceProvider => new FileUsageSessionStore( serviceProvider ) );

    /// <summary>
    /// Registers a <see cref="RegistryUsageSessionStore"/>, which keeps the record of each project in a value of a
    /// registry key. Away from Windows, it registers a <see cref="FileUsageSessionStore"/> instead.
    /// </summary>
    /// <param name="serviceProviderBuilder">The builder.</param>
    /// <param name="hive">The hive of the key.</param>
    /// <param name="keyPath">The path of the key, which must hold nothing but these records.</param>
    /// <remarks>
    /// The registry store requires the <see cref="IRegistryService"/> that
    /// <see cref="RegisterConfigurationServices.AddRegistryConfigurationServices"/> registers on Windows.
    /// </remarks>
    public static ServiceProviderBuilder AddRegistryUsageSessionStore(
        this ServiceProviderBuilder serviceProviderBuilder,
        RegistryHiveKind hive,
        string keyPath )
    {
        if ( !RuntimeInformation.IsOSPlatform( OSPlatform.Windows ) )
        {
            return serviceProviderBuilder.AddFileUsageSessionStore();
        }

        return serviceProviderBuilder.AddSingleton<IUsageSessionStore>( serviceProvider => new RegistryUsageSessionStore( serviceProvider, hive, keyPath ) );
    }
}
