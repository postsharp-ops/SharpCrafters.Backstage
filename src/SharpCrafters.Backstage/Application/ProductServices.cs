// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Extensibility;
using SharpCrafters.Backstage.Licensing;
using SharpCrafters.Backstage.Licensing.Consumption;
using SharpCrafters.Backstage.Licensing.Registration;
using SharpCrafters.Backstage.Telemetry;
using SharpCrafters.Backstage.UserInterface;
using System;
using System.Threading;

namespace SharpCrafters.Backstage.Application;

/// <summary>
/// The services of one product of a composite product. Each service is created when it is first requested.
/// </summary>
/// <remarks>
/// The standard services of the primary product cannot be resolved from the outer service provider, because the
/// composite services replace them there, so they are created here from the outer provider. The services of another
/// product are resolved from its inner provider, except <see cref="IIdeExtensionStatusService"/>, which an inner
/// provider does not register because it has no user interface.
/// </remarks>
internal sealed class ProductServices
{
    private readonly Lazy<IServiceProvider> _serviceProvider;
    private readonly Lazy<ILicenseRegistrationService> _licenseRegistrationService;
    private readonly Lazy<ILicenseConsumptionService> _licenseConsumptionService;
    private readonly Lazy<ITelemetryConfigurationService> _telemetryConfigurationService;
    private readonly Lazy<IIdeExtensionStatusService> _ideExtensionStatusService;

    public ProductServices(
        BackstageProduct product,
        bool isPrimary,
        Func<IServiceProvider> createServiceProvider,
        BackstageInitializationOptions options )
    {
        this.Product = product;
        this.IsPrimary = isPrimary;

        this._serviceProvider = new Lazy<IServiceProvider>( createServiceProvider, LazyThreadSafetyMode.ExecutionAndPublication );

        this._licenseRegistrationService = new Lazy<ILicenseRegistrationService>(
            () => isPrimary
                ? new LicenseRegistrationService( this.ServiceProvider )
                : this.ServiceProvider.GetRequiredBackstageService<ILicenseRegistrationService>(),
            LazyThreadSafetyMode.ExecutionAndPublication );

        this._licenseConsumptionService = new Lazy<ILicenseConsumptionService>(
            () => isPrimary
                ? LicenseConsumptionServiceFactory.Create( this.ServiceProvider, options.LicensingOptions )
                : this.ServiceProvider.GetRequiredBackstageService<ILicenseConsumptionService>(),
            LazyThreadSafetyMode.ExecutionAndPublication );

        this._telemetryConfigurationService = new Lazy<ITelemetryConfigurationService>(
            () =>
            {
                if ( isPrimary )
                {
                    // The outer provider initializes it, through the composite service.
                    return new TelemetryConfigurationService( this.ServiceProvider );
                }
                else
                {
                    // The inner provider is not initialized, so its service is initialized here.
                    var service = this.ServiceProvider.GetRequiredBackstageService<ITelemetryConfigurationService>();
                    service.Initialize();

                    return service;
                }
            },
            LazyThreadSafetyMode.ExecutionAndPublication );

        this._ideExtensionStatusService = new Lazy<IIdeExtensionStatusService>(
            () => new IdeExtensionStatusService( this.ServiceProvider ),
            LazyThreadSafetyMode.ExecutionAndPublication );
    }

    public BackstageProduct Product { get; }

    public ILicenseProductCatalog Catalog => this.Product.LicenseProductCatalog;

    public bool IsPrimary { get; }

    /// <summary>
    /// Gets the service provider of the product: the outer service provider, seen through the catalog of the product,
    /// for the primary product, or the inner service provider for another product.
    /// </summary>
    public IServiceProvider ServiceProvider => this._serviceProvider.Value;

    public bool IsServiceProviderCreated => this._serviceProvider.IsValueCreated;

    public ILicenseRegistrationService LicenseRegistrationService => this._licenseRegistrationService.Value;

    public ILicenseConsumptionService LicenseConsumptionService => this._licenseConsumptionService.Value;

    public ITelemetryConfigurationService TelemetryConfigurationService => this._telemetryConfigurationService.Value;

    public IIdeExtensionStatusService IdeExtensionStatusService => this._ideExtensionStatusService.Value;
}
