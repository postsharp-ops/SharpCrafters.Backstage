// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Diagnostics;
using SharpCrafters.Backstage.Extensibility;
using SharpCrafters.Backstage.Windows.Commands;
using System;

namespace SharpCrafters.Backstage.Windows;

/// <summary>
/// Creates the Backstage services of the notifier process from the application info that the product supplies to
/// <see cref="BackstageDesktopProgram.Run"/>, and holds them for the lifetime of the process.
/// </summary>
/// <remarks>
/// The notifier is a process of its own, so one provider per process is the lifetime it needs. The provider is held
/// here rather than in the process-wide <see cref="BackstageServiceFactory"/> provider, which is obsolete.
/// </remarks>
internal static class DesktopServices
{
    private static readonly object _sync = new();
    private static BackstageDesktopApplicationInfo? _applicationInfo;
    private static IServiceProvider? _serviceProvider;

    /// <summary>
    /// Gets the service provider when it has been created, or <c>null</c> when no command has requested it yet.
    /// </summary>
    public static IServiceProvider? ServiceProvider => _serviceProvider;

    /// <summary>
    /// Stores the application info. It is called once by <see cref="BackstageDesktopProgram.Run"/>.
    /// </summary>
    public static void Initialize( BackstageDesktopApplicationInfo applicationInfo )
    {
        if ( _applicationInfo != null )
        {
            throw new InvalidOperationException( "The desktop services have already been initialized." );
        }

        _applicationInfo = applicationInfo;
    }

    /// <summary>
    /// Gets the service provider, creating it on the first call with the settings of the command.
    /// </summary>
    /// <exception cref="InvalidOperationException"><see cref="Initialize"/> has not been called.</exception>
    public static IServiceProvider Get( BaseSettings settings )
    {
        var applicationInfo = _applicationInfo
                              ?? throw new InvalidOperationException(
                                  $"{nameof(BackstageDesktopProgram)}.{nameof(BackstageDesktopProgram.Run)} has not been called." );

        lock ( _sync )
        {
            if ( _serviceProvider != null )
            {
                return _serviceProvider;
            }

            var serviceProvider = BackstageServiceFactory.CreateServiceProvider(
                new BackstageInitializationOptions( applicationInfo, applicationInfo.Product )
                {
                    AddLicensing = false,
                    IsDevelopmentEnvironment = settings.IsDevelopmentEnvironment,
                    AddSupportServices = true,
                    AddUserInterface = true,

                    // We don't want to open more toast notifications.
                    DetectToastNotifications = false
                } );

            var logger = serviceProvider.GetLoggerFactory().GetLogger( "App" );
            logger.Trace?.Log( $"Executing: {string.Join( ' ', Environment.GetCommandLineArgs() )}" );

            _serviceProvider = serviceProvider;

            return serviceProvider;
        }
    }
}
