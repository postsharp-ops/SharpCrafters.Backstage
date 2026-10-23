// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Configuration.Registry;
using SharpCrafters.Backstage.Diagnostics;
using SharpCrafters.Backstage.Extensibility;
using SharpCrafters.Backstage.FileLocks;
using SharpCrafters.Backstage.Infrastructure;
using SharpCrafters.Backstage.Licensing;
using SharpCrafters.Backstage.Maintenance;
using SharpCrafters.Backstage.ProcessClassification;
using SharpCrafters.Backstage.Serialization;
using SharpCrafters.Backstage.Tools;
using SharpCrafters.Backstage.UserInterface;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace SharpCrafters.Backstage.Application;

/// <summary>
/// Holds the services of each product of a composite product. The services of the primary product are created from the
/// outer service provider, and the services of every other product from an inner service provider of its own, which is
/// created when it is first needed and disposed with the current object.
/// </summary>
internal sealed class CompositeProductServices : IBackstageService, IDisposable
{
    /// <summary>
    /// The services that do not depend on the product. An inner provider uses the instance of the outer provider, when
    /// the outer provider has one, instead of creating its own.
    /// </summary>
    /// <remarks>
    /// The named lock service, the temporary file manager, the configuration manager and the standard directories are
    /// not in this list, because they depend on the profile of the product: a product names its locks and its
    /// directories after itself.
    /// </remarks>
    private static readonly Type[] _sharedServiceTypes =
    [
        typeof(EarlyLoggerFactory),
        typeof(ILoggerFactory),
        typeof(IDateTimeProvider),
        typeof(IHttpClientFactory),
        typeof(IJsonSerializationService),
        typeof(BackstageBackgroundTasksService),
        typeof(IEventDispatcher),
        typeof(IEventDispatcherObserver),
        typeof(IMachineIdProvider),
        typeof(IRegistryService),
        typeof(IFileSystem),
        typeof(IEnvironmentVariableProvider),
        typeof(IRuntimeInformation),
        typeof(IUserIdentityProvider),
        typeof(IUserDeviceDetectionService),
        typeof(IProcessExecutor),
        typeof(IRecoverableExceptionService),
        typeof(IParentProcessSearch),
        typeof(IContainerDetector),
        typeof(IUnattendedProcessDetector),
        typeof(ILockingProcessDetector),
        typeof(IProcessManager)
    ];

    /// <summary>
    /// The services that an inner provider never registers, in addition to those that its options disable.
    /// </summary>
    /// <remarks>
    /// The crash dump service is registered with the diagnostics, which an inner provider needs for its telemetry and
    /// license audit services. The dumper of the outer provider is enough for the process.
    /// </remarks>
    private static readonly Type[] _suppressedServiceTypes = [typeof(IMiniDumper), typeof(IBackstageToolsExtractor)];

    private readonly IServiceProvider _serviceProvider;
    private readonly BackstageInitializationOptions _options;
    private readonly object _sync = new();
    private bool _isDisposed;

    public CompositeProductServices( IServiceProvider serviceProvider, ImmutableArray<BackstageProduct> products, BackstageInitializationOptions options )
    {
        this._serviceProvider = serviceProvider;
        this._options = options;

        this.Products = products.Select(
                ( product, index ) => index == 0
                    ? new ProductServices( product, true, () => new PrimaryServiceProvider( serviceProvider, product.LicenseProductCatalog ), options )
                    : new ProductServices( product, false, () => this.CreateInnerServiceProvider( product ), options ) )
            .ToImmutableArray();
    }

    /// <summary>
    /// Gets the services of each product, the primary product first.
    /// </summary>
    public ImmutableArray<ProductServices> Products { get; }

    public ProductServices Primary => this.Products[0];

    /// <summary>
    /// Gets the services of the product to which a license product is native, or of the first product whose family
    /// includes it, or of the primary product.
    /// </summary>
    public ProductServices GetOwner( LicenseProduct licenseProduct )
        => this.Products.FirstOrDefault( p => p.Catalog.IsNativeProduct( licenseProduct ) )
           ?? this.Products.FirstOrDefault( p => p.Catalog.IsProductOfFamily( licenseProduct ) )
           ?? this.Primary;

    private IServiceProvider CreateInnerServiceProvider( BackstageProduct product )
    {
        lock ( this._sync )
        {
            if ( this._isDisposed )
            {
                throw new ObjectDisposedException( nameof(CompositeProductServices) );
            }

            var innerBuilder = new SimpleServiceProviderBuilder();
            var excludedTypes = new HashSet<Type>( _suppressedServiceTypes );

            // The instances of the outer provider are registered first, as instances that the inner provider does not
            // own, and the registrations of AddBackstageServices for these types are then ignored.
            foreach ( var type in _sharedServiceTypes )
            {
                var instance = this._serviceProvider.GetService( type );

                if ( instance != null )
                {
                    innerBuilder.AddService( type, instance );
                    excludedTypes.Add( type );
                }
            }

            var filteringBuilder = new ServiceProviderBuilder(
                ( type, factory ) =>
                {
                    if ( !excludedTypes.Contains( type ) )
                    {
                        innerBuilder.AddService( type, factory );
                    }
                },
                ( type, instance ) =>
                {
                    if ( !excludedTypes.Contains( type ) )
                    {
                        innerBuilder.AddService( type, instance );
                    }
                } );

            // The inner provider is not initialized with InitializeBackstageServices: the outer provider already runs the
            // background services, the upload and the shutdown hooks of the process.
            filteringBuilder.AddBackstageServices(
                new BackstageInitializationOptions( this._options.ApplicationInfo, product )
                {
                    AddSupportServices = this._options.AddSupportServices,
                    AddLicensing = this._options.AddLicensing,
                    LicensingOptions = this._options.LicensingOptions with { ProductCatalog = null, BuildTestLicenseAction = null },
                    IsDevelopmentEnvironment = this._options.IsDevelopmentEnvironment,
                    DiagnosticsOptions = this._options.DiagnosticsOptions,
                    AdditionalJsonTypeInfoResolvers = this._options.AdditionalJsonTypeInfoResolvers,
                    AddUserInterface = false,
                    AddDumperService = false,
                    AddToolsExtractor = null,
                    OpenWelcomePage = false,
                    DetectToastNotifications = false,
                    AddRssClient = false,
                    AutoUploadTelemetry = false
                } );

            return innerBuilder.ServiceProvider;
        }
    }

    /// <summary>
    /// Disposes the inner service providers that have been created.
    /// </summary>
    public void Dispose()
    {
        lock ( this._sync )
        {
            if ( this._isDisposed )
            {
                return;
            }

            this._isDisposed = true;
        }

        CompositeBackstageProduct.ForEach(
            this.Products.Where( p => !p.IsPrimary && p.IsServiceProviderCreated ),
            p => (p.ServiceProvider as IDisposable)?.Dispose() );
    }

    /// <summary>
    /// The outer service provider as the primary product sees it: the license product catalog is the catalog of the
    /// primary product and not the union catalog, so that the licensing services of the primary product read and write
    /// the license keys of that product only.
    /// </summary>
    private sealed class PrimaryServiceProvider : IServiceProvider
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILicenseProductCatalog _catalog;

        public PrimaryServiceProvider( IServiceProvider serviceProvider, ILicenseProductCatalog catalog )
        {
            this._serviceProvider = serviceProvider;
            this._catalog = catalog;
        }

        public object? GetService( Type serviceType )
            => serviceType == typeof(ILicenseProductCatalog) ? this._catalog : this._serviceProvider.GetService( serviceType );
    }
}
