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
using SharpCrafters.Backstage.Utilities;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace SharpCrafters.Backstage.Application.Composite;

/// <summary>
/// Holds the members of a composite product, one for each product, and chooses the member that handles a license. The
/// services of the primary member are created from the outer service provider, and those of every other member from an
/// inner service provider of its own, which is created when it is first needed and disposed with the current object.
/// </summary>
internal sealed class CompositeProductMembers : IBackstageService, IDisposable
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

    public CompositeProductMembers( IServiceProvider serviceProvider, ImmutableArray<BackstageProduct> products, BackstageInitializationOptions options )
    {
        this._serviceProvider = serviceProvider;
        this._options = options;

        this.Members = products.Select(
                ( product, index ) => index == 0
                    ? new CompositeProductMember( product, true, () => serviceProvider, options )
                    : new CompositeProductMember( product, false, () => this.CreateInnerServiceProvider( product ), options ) )
            .ToImmutableArray();
    }

    /// <summary>
    /// Gets the outer service provider.
    /// </summary>
    public IServiceProvider ServiceProvider => this._serviceProvider;

    /// <summary>
    /// Gets the members of the composite product, the primary product first.
    /// </summary>
    public ImmutableArray<CompositeProductMember> Members { get; }

    public CompositeProductMember Primary => this.Members[0];

    /// <summary>
    /// Gets the member whose product a license product is native to, or the first member whose product family includes
    /// it, or the primary member.
    /// </summary>
    public CompositeProductMember GetMemberForProduct( LicenseProduct licenseProduct )
        => this.Members.FirstOrDefault( p => p.Catalog.IsNativeProduct( licenseProduct ) )
           ?? this.Members.FirstOrDefault( p => p.Catalog.IsProductOfFamily( licenseProduct ) )
           ?? this.Primary;

    /// <summary>
    /// Gets the member that handles a license string: for a license key, the member of its license product (see
    /// <see cref="GetMemberForProduct(LicenseProduct)"/>); for the URL of a license server or a string that is not a
    /// license key, the primary member.
    /// </summary>
    /// <remarks>
    /// The license key is cleaned as the standard services clean it before they read it, so that a key pasted with
    /// line breaks, spaces or quotes goes to the same product as the key itself.
    /// </remarks>
    public CompositeProductMember GetMemberForLicense( string? licenseString )
    {
        if ( string.IsNullOrWhiteSpace( licenseString ) || LicenseServerUrl.IsLicenseServerUrl( licenseString ) )
        {
            return this.Primary;
        }

        return LicenseKeyData.TryDeserialize( License.CleanLicenseKey( licenseString! ), out var licenseKeyData, out _ )
            ? this.GetMemberForProduct( licenseKeyData.Product )
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
                throw new ObjectDisposedException( nameof(CompositeProductMembers) );
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

        innerServiceProviders.ForEachContinueOnException( p => p.Dispose() );
    }
}
