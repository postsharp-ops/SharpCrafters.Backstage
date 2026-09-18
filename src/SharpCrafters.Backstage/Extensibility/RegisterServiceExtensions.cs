// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Configuration;
using SharpCrafters.Backstage.Licensing;
using SharpCrafters.Backstage.Serialization;
using SharpCrafters.Backstage.Telemetry;
using SharpCrafters.Backstage.UserInterface;
using System;
using System.Collections.Generic;
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

        // Everything that one product answers differently from another is registered here, by the product, and
        // nothing of it is assumed on its behalf. Where the configurations live and how an audit is identified are
        // both decided this way: see BackstageProduct.RegisterServices, which says which services a product owes.
        product.RegisterServices.Invoke( serviceProviderBuilder );

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
