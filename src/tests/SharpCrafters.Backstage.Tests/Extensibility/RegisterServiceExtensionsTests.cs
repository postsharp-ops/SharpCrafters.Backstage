// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using Metalama.Backstage;
using Metalama.Backstage.Tools;
using Microsoft.Extensions.DependencyInjection;
using SharpCrafters.Backstage.Diagnostics;
using SharpCrafters.Backstage.Extensibility;
using SharpCrafters.Backstage.Infrastructure;
using SharpCrafters.Backstage.Licensing.Audit;
using SharpCrafters.Backstage.Licensing.Consumption;
using SharpCrafters.Backstage.Telemetry;
using SharpCrafters.Backstage.Testing;
using SharpCrafters.Backstage.Tools;
using SharpCrafters.Backstage.UserInterface;
using SharpCrafters.Backstage.UserInterface.Toasts;
using System;
using Xunit;

namespace SharpCrafters.Backstage.Tests.Extensibility;

public sealed class RegisterServiceExtensionsTests
{
    private static ServiceCollectionBuilder CreateServiceCollectionBuilder() => new();

    [Theory]
    [InlineData( true, true, true )]
    [InlineData( false, true, true )]
    [InlineData( false, false, true, true, true, false )]
    [InlineData( true, true, false )]
    [InlineData( false, true, false )]
    [InlineData( false, false, false, true, true, false )]
    [InlineData( true, false, true, true, true, false )]
    [InlineData( true, true, true, false, false )]
    public void AddBackstageServices(
        bool addLicensing,
        bool addSupportServices,
        bool addUserInterface,
        bool disableLicenseAudit = true,
        bool addToolsExtractor = true,
        bool addRssClient = true )
    {
        var options =
            new BackstageInitializationOptions(
                new TestApplicationInfo( "Test", true, "1.0", DateTime.Today ) { IsLicenseAuditEnabled = !disableLicenseAudit },
                MetalamaProduct.Instance )
            {
                AddLicensing = addLicensing,
                AddSupportServices = addSupportServices,
                AddUserInterface = addUserInterface,
                AddRssClient = addRssClient,
                DetectToastNotifications = addUserInterface
            };

        if ( addToolsExtractor && (addSupportServices || addUserInterface) )
        {
            options = options with
            {
                AddToolsExtractor = b => b.AddService(
                    typeof(IBackstageToolsExtractor),
                    p => new BackstageToolsExtractor( p, typeof(BackstageToolsExtensions).Assembly ) )
            };
        }

        var serviceProviderBuilder = CreateServiceCollectionBuilder();
        serviceProviderBuilder.AddBackstageServices( options );
        var serviceProvider = serviceProviderBuilder.ServiceCollection.BuildServiceProvider();
        Assert.NotNull( serviceProvider.GetBackstageService<IPlatformInfo>() );

        if ( addLicensing )
        {
            Assert.NotNull( serviceProvider.GetBackstageService<ILicenseConsumptionService>() );

            if ( disableLicenseAudit )
            {
                Assert.Null( serviceProvider.GetBackstageService<ILicenseAuditManager>() );
            }
            else
            {
                Assert.NotNull( serviceProvider.GetBackstageService<ILicenseAuditManager>() );
            }
        }
        else
        {
            Assert.Null( serviceProvider.GetBackstageService<ILicenseConsumptionService>() );
            Assert.Null( serviceProvider.GetBackstageService<ILicenseAuditManager>() );
        }

        if ( addSupportServices )
        {
            Assert.NotNull( serviceProvider.GetBackstageService<ILoggerFactory>() );
            Assert.NotNull( serviceProvider.GetBackstageService<ITelemetryUploader>() );
            Assert.NotNull( serviceProvider.GetBackstageService<IExceptionReportManager>() );
            Assert.NotNull( serviceProvider.GetBackstageService<IUsageSessionFactory>() );
        }
        else
        {
            Assert.Null( serviceProvider.GetBackstageService<ILoggerFactory>() );
            Assert.Null( serviceProvider.GetBackstageService<IExceptionReportManager>() );
            Assert.Null( serviceProvider.GetBackstageService<IUsageSessionFactory>() );
            Assert.Null( serviceProvider.GetBackstageService<ITelemetryUploader>() );
        }

        if ( addUserInterface )
        {
            Assert.NotNull( serviceProvider.GetBackstageService<IUserInterfaceService>() );
            Assert.NotNull( serviceProvider.GetBackstageService<IToastNotificationService>() );
            Assert.NotNull( serviceProvider.GetBackstageService<IToastNotificationDetectionService>() );
        }
        else
        {
            Assert.Null( serviceProvider.GetBackstageService<IToastNotificationService>() );
            Assert.Null( serviceProvider.GetBackstageService<IUserInterfaceService>() );
            Assert.Null( serviceProvider.GetBackstageService<IToastNotificationDetectionService>() );
        }

        if ( addToolsExtractor && (addSupportServices || addUserInterface) )
        {
            Assert.NotNull( serviceProvider.GetBackstageService<IBackstageToolsExtractor>() );
        }
        else
        {
            Assert.Null( serviceProvider.GetBackstageService<IBackstageToolsExtractor>() );
        }
    }
}