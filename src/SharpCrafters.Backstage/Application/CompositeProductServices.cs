// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Diagnostics;
using SharpCrafters.Backstage.Extensibility;
using SharpCrafters.Backstage.Licensing;
using SharpCrafters.Backstage.Licensing.LicenseServer;
using SharpCrafters.Backstage.Licensing.Licenses;
using SharpCrafters.Backstage.ProcessClassification;
using SharpCrafters.Backstage.Telemetry;
using SharpCrafters.Backstage.Tools;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;

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
    /// <para>
    /// The crash dump service is registered with the diagnostics, which an inner provider needs for its telemetry and
    /// license audit services. The dumper of the outer provider is enough for the process.
    /// </para>
    /// <para>
    /// The analytics uploader is optional for its consumers, so an inner provider sends no analytics directly. With
    /// <see cref="QueueOnlyTelemetryUploader"/>, this keeps the telemetry of a secondary product in its queue, where the
    /// processes of that product upload it.
    /// </para>
    /// </remarks>
    private static readonly Type[] _suppressedServiceTypes = [typeof(IMiniDumper), typeof(IBackstageToolsExtractor), typeof(MatomoUploader)];

    private readonly IServiceProvider _serviceProvider;
    private readonly BackstageInitializationOptions _options;
    private readonly object _sync = new();
    private readonly List<IDisposable> _innerServiceProviders = [];
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
    /// Gets the outer service provider.
    /// </summary>
    public IServiceProvider ServiceProvider => this._serviceProvider;

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

    /// <summary>
    /// Gets the services of the product that owns a license string: the owner of the product of a license key (see
    /// <see cref="GetOwner(LicenseProduct)"/>), or the primary product for the URL of a license server and for a string
    /// that is not a license key.
    /// </summary>
    /// <remarks>
    /// The license key is cleaned as the standard services clean it before they read it, so that a key pasted with
    /// line breaks, spaces or quotes goes to the same product as the key itself.
    /// </remarks>
    public ProductServices GetOwner( string? licenseString )
    {
        if ( string.IsNullOrWhiteSpace( licenseString ) || LicenseServerUrl.IsLicenseServerUrl( licenseString ) )
        {
            return this.Primary;
        }

        return LicenseKeyData.TryDeserialize( License.CleanLicenseKey( licenseString! ), out var licenseKeyData, out _ )
            ? this.GetOwner( licenseKeyData.Product )
            : this.Primary;
    }

    private IServiceProvider CreateInnerServiceProvider( BackstageProduct product )
    {
        // The whole creation is under the lock, and so is Dispose, so a provider is either never created or recorded
        // before Dispose reads the record.
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

                    if ( type == typeof(ITelemetryUploader) )
                    {
                        innerBuilder.AddService( type, QueueOnlyTelemetryUploader.Instance );
                    }
                    else if ( this.GetSharedInstance( type ) is { } sharedInstance )
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

            var serviceProvider = innerBuilder.ServiceProvider;
            this._innerServiceProviders.Add( (IDisposable) serviceProvider );

            // The inner provider is not initialized with InitializeBackstageServices: the outer provider already runs the
            // background services and the shutdown hooks of the process. Only the services that throw until they are
            // initialized are initialized here, in the order of InitializeBackstageServices, so that every service of the
            // inner provider can use them, and not only the composite services.
            serviceProvider.GetBackstageService<IUnattendedProcessDetector>()?.Initialize();
            serviceProvider.GetBackstageService<ITelemetryConfigurationService>()?.Initialize();

            return serviceProvider;
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
        IDisposable[] innerServiceProviders;

        lock ( this._sync )
        {
            if ( this._isDisposed )
            {
                return;
            }

            this._isDisposed = true;
            innerServiceProviders = this._innerServiceProviders.ToArray();
            this._innerServiceProviders.Clear();
        }

        CompositeBackstageProduct.ForEach( innerServiceProviders, p => p.Dispose() );
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

    /// <summary>
    /// The telemetry uploader of an inner provider. It uploads nothing, so that a report of a secondary product stays in
    /// the upload queue of that product, and the outer provider runs the only upload of the process.
    /// </summary>
    private sealed class QueueOnlyTelemetryUploader : ITelemetryUploader
    {
        public static QueueOnlyTelemetryUploader Instance { get; } = new();

        private QueueOnlyTelemetryUploader() { }

        public bool StartUpload( bool force = false ) => false;

        public Task UploadAsync() => Task.CompletedTask;
    }
}
