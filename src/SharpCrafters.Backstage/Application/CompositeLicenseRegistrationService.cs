// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Licensing.LicenseServer;
using SharpCrafters.Backstage.Licensing.Licenses;
using SharpCrafters.Backstage.Licensing.Registration;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SharpCrafters.Backstage.Application;

/// <summary>
/// The <see cref="ILicenseRegistrationService"/> of a composite product. It reports the licenses and the editions of
/// every product, and routes each registration to the store of one product.
/// </summary>
/// <remarks>
/// <para>
/// A license key is routed to the product that owns the product of the key (see
/// <see cref="CompositeProductServices.GetOwner"/>), which is the product to which it is native. A key that two
/// families accept, such as a PostSharp Ultimate key, is native to one family only, so it goes to that family. A license
/// server URL, and a string that is not a license key, go to the primary product, which reports the error of a string
/// that it cannot register.
/// </para>
/// <para>
/// The interface removes licenses all at once, without naming a key, so <see cref="RemoveLicenses"/> removes the
/// licenses of every product.
/// </para>
/// </remarks>
internal sealed class CompositeLicenseRegistrationService : ILicenseRegistrationService
{
    private readonly CompositeProductServices _products;

    public CompositeLicenseRegistrationService( CompositeProductServices products )
    {
        this._products = products;

        foreach ( var product in products.Products )
        {
            product.LicenseRegistrationService.PropertyChanged += this.OnPropertyChanged;
        }
    }

    private IEnumerable<ILicenseRegistrationService> Services => this._products.Products.Select( p => p.LicenseRegistrationService );

    private ILicenseRegistrationService Primary => this._products.Primary.LicenseRegistrationService;

    private void OnPropertyChanged( object? sender, PropertyChangedEventArgs e ) => this.PropertyChanged?.Invoke( this, e );

    private ILicenseRegistrationService GetService( string licenseString )
    {
        if ( !LicenseServerUrl.IsLicenseServerUrl( licenseString )
             && LicenseKeyData.TryDeserialize( licenseString, out var licenseKeyData, out _ ) )
        {
            return this._products.GetOwner( licenseKeyData.Product ).LicenseRegistrationService;
        }
        else
        {
            return this.Primary;
        }
    }

    public LicenseRegistrationResult Register( SelfRegisteredEdition edition, SelfRegisteredEditionOptions? options = null )
    {
        // An edition is compared by identity, as the standard service does. An edition of no product goes to the primary
        // product, which reports that it is not one of its editions.
        var product = this._products.Products.FirstOrDefault( p => p.Catalog.SelfRegisteredEditions.Contains( edition ) ) ?? this._products.Primary;

        return product.LicenseRegistrationService.Register( edition, options );
    }

    public ImmutableArray<SelfRegisteredEdition> AvailableEditions => this.Services.SelectMany( s => s.AvailableEditions ).ToImmutableArray();

    public ValueTask<LicenseRegistrationResult> RegisterLicenseAsync( string licenseString, CancellationToken cancellationToken = default )
        => this.GetService( licenseString ).RegisterLicenseAsync( licenseString, cancellationToken );

    [Obsolete( "Use RegisterLicenseAsync." )]
    public LicenseRegistrationResult RegisterLicense( string licenseString ) => this.GetService( licenseString ).RegisterLicense( licenseString );

    public void RemoveLicenses() => CompositeBackstageProduct.ForEach( this.Services, s => s.RemoveLicenses() );

    public IEnumerable<LicenseRegistrationProperties> RegisteredLicenses => this.Services.SelectMany( s => s.RegisteredLicenses );

    public IEnumerable<Version> UnsupportedRegisteredLicenseVersions
        => this.Services.SelectMany( s => s.UnsupportedRegisteredLicenseVersions ).Distinct().OrderBy( v => v );

    public ValueTask<LicenseRegistrationResult> ValidateLicenseKeyAsync( string licenseKey, CancellationToken cancellationToken = default )
        => this.GetService( licenseKey ).ValidateLicenseKeyAsync( licenseKey, cancellationToken );

    /// <inheritdoc />
    /// <remarks>
    /// The lease is acquired from the license server of the first product that has one registered, or by the primary
    /// product, which reports that none is registered.
    /// </remarks>
    public ValueTask<LicenseRegistrationResult> AcquireLeaseAsync( bool forceRenewal = false, CancellationToken cancellationToken = default )
    {
        var service = this.Services.FirstOrDefault( s => s.RegisteredLicenses.Any( l => l.LicenseServerUrl != null ) ) ?? this.Primary;

        return service.AcquireLeaseAsync( forceRenewal, cancellationToken );
    }

    [Obsolete( "Use ValidateLicenseKeyAsync." )]
    public LicenseRegistrationResult ValidateLicenseKey( string licenseKey ) => this.GetService( licenseKey ).ValidateLicenseKey( licenseKey );

    public ValueTask<LicenseRegistrationResult> ResolveLicenseAsync( string licenseString, CancellationToken cancellationToken = default )
        => this.GetService( licenseString ).ResolveLicenseAsync( licenseString, cancellationToken );

    [Obsolete( "Use ResolveLicenseAsync." )]
    public LicenseRegistrationResult ParseLicenseKey( string licenseKey ) => this.GetService( licenseKey ).ParseLicenseKey( licenseKey );

    public event PropertyChangedEventHandler? PropertyChanged;
}
