// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Diagnostics;
using SharpCrafters.Backstage.Extensibility;
using SharpCrafters.Backstage.Licensing;
using SharpCrafters.Backstage.Tools;
using System;
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

            // A service type marked as product-neutral is registered as the instance of the outer provider, which the
            // inner provider does not own and does not dispose. When the outer provider has no instance of the type,
            // the registration of the inner provider is kept.
            var filteringBuilder = new ServiceProviderBuilder(
                ( type, factory ) =>
                {
                    if ( _suppressedServiceTypes.Contains( type ) )
                    {
                        return;
                    }

                    if ( this.GetSharedInstance( type ) is { } sharedInstance )
                    {
                        innerBuilder.AddService( type, sharedInstance );
                    }
                    else
                    {
                        innerBuilder.AddService( type, factory );
                    }
                },
                ( type, instance ) =>
                {
                    if ( _suppressedServiceTypes.Contains( type ) )
                    {
                        return;
                    }

                    innerBuilder.AddService( type, this.GetSharedInstance( type ) ?? instance );
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
    /// Gets the instance of the outer provider for a service type marked with
    /// <see cref="ProductNeutralServiceAttribute"/>, or <see langword="null"/> when the type is not marked or the outer
    /// provider has no instance of it.
    /// </summary>
    private object? GetSharedInstance( Type serviceType )
        => serviceType.IsDefined( typeof(ProductNeutralServiceAttribute), false ) ? this._serviceProvider.GetService( serviceType ) : null;

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
