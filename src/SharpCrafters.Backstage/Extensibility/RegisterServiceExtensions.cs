// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Configuration;
using SharpCrafters.Backstage.Licensing;
using SharpCrafters.Backstage.Licensing.Audit;
using SharpCrafters.Backstage.Serialization;
using SharpCrafters.Backstage.Telemetry;
using SharpCrafters.Backstage.UserInterface;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization.Metadata;

namespace SharpCrafters.Backstage.Extensibility;

/// <summary>
/// Extension methods for setting up the Backstage services in a <see cref="ServiceProviderBuilder" />. This is the
/// umbrella over the registration methods of the packages: <see cref="RegisterCoreServices.AddCoreServices"/>,
/// <see cref="RegisterConfigurationServices.AddConfigurationServices"/>,
/// <see cref="RegisterTelemetryServices.AddTelemetryServices"/>, <see cref="RegisterLicensingServices.AddLicensingServices"/>
/// and <see cref="RegisterUserInterfaceServices.AddUserInterfaceServices"/>.
/// </summary>
public static class RegisterServiceExtensions
{
    /// <summary>
    /// Registers the services selected by the options.
    /// </summary>
    public static void AddBackstageServices( this ServiceProviderBuilder serviceProviderBuilder, BackstageInitializationOptions options )
    {
        var applicationInfo = options.ApplicationInfo;

        var jsonTypeInfoResolvers = new List<IJsonTypeInfoResolver> { BackstageJsonContext.Default };
        jsonTypeInfoResolvers.AddRange( options.AdditionalJsonTypeInfoResolvers );

        var product = options.Product;

        var coreOptions = new CoreInitializationOptions( product.Profile, applicationInfo )
        {
            AddDiagnostics = options.AddSupportServices,
            AddDumper = options.AddDumperService,
            AddTools = options.AddSupportServices || options.AddUserInterface,
            IsDevelopmentEnvironment = options.IsDevelopmentEnvironment,
            AddToolsExtractor = options.AddToolsExtractor,
            DiagnosticsOptions = options.DiagnosticsOptions,
            JsonTypeInfoResolvers = jsonTypeInfoResolvers
        };

        serviceProviderBuilder.AddCoreServices( coreOptions );

        // The defaults of the services that a product may answer differently. They are registered first and the
        // services of the product after them, so that one of the same type replaces the default: a product that says
        // nothing gets these, and a product that says something gets what it said.
        //
        // The configurations go in files, which is what a product that shares nothing with an earlier version of
        // itself wants. A product that shares some of them calls AddRegistryConfigurationServices itself.
        serviceProviderBuilder.AddConfigurationServices();

        // An audit is throttled by the content of its report, so that a report is sent again whenever anything in it
        // changes. A product whose earlier versions keep the record and share it with this one has to key it the way
        // they key it, and says so by registering its own.
        serviceProviderBuilder.AddSingleton<ILicenseAuditKeyProvider>( _ => ReportContentLicenseAuditKeyProvider.Instance );

        product.RegisterServices?.Invoke( serviceProviderBuilder );

        if ( options.AddSupportServices )
        {
            serviceProviderBuilder.AddTelemetryServices( product.TelemetryOptions );
        }

        var userInterfaceOptions = product.UserInterfaceOptions with
        {
            OpenWelcomePage = options.OpenWelcomePage,
            DetectToastNotifications = options.DetectToastNotifications,
            AddRssClient = options.AddRssClient
        };

        if ( options.AddRssClient && !options.AddSupportServices )
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                $"{nameof(options.AddRssClient)} requires {nameof(options.AddSupportServices)}." );
        }

        if ( options.AddUserInterface )
        {
            serviceProviderBuilder.AddUserInterfaceServices( userInterfaceOptions, product.WebLinks );
        }
        else if ( options.AddRssClient )
        {
            serviceProviderBuilder.AddRssClientServices( userInterfaceOptions, product.WebLinks );
        }

        if ( options.AddLicensing )
        {
            if ( applicationInfo.IsLicenseAuditEnabled && !options.AddSupportServices )
            {
                throw new InvalidOperationException( "License audit requires support services." );
            }

            var licensingOptions = options.LicensingOptions.ProductCatalog != null
                ? options.LicensingOptions
                : options.LicensingOptions with { ProductCatalog = product.LicenseProductCatalog };

            serviceProviderBuilder.AddLicensingServices( licensingOptions, applicationInfo );
        }

        serviceProviderBuilder.AddSingleton( serviceProvider => new BackstageServicesInitializer( serviceProvider, options ) );
    }
}
