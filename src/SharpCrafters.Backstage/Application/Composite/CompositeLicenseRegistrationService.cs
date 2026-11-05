// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Diagnostics;
using SharpCrafters.Backstage.Licensing.Registration;
using SharpCrafters.Backstage.Telemetry;
using SharpCrafters.Backstage.Utilities;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SharpCrafters.Backstage.Application.Composite;

/// <summary>
/// The <see cref="ILicenseRegistrationService"/> of a composite product. It reports the licenses and the editions of
/// every product, and registers a license key in the store of every product that accepts it.
/// </summary>
/// <remarks>
/// <para>
/// A license key is registered in the store of every product whose family accepts its product (see
/// <see cref="CompositeProductMembers.GetMembersAcceptingLicense(string)"/>), so that a PostSharp Ultimate key, which
/// Metalama also accepts, is seen by the builds of both products. The product to which the key is native registers it
/// first, and its result is the result of the registration. When another product cannot register the key, the failure
/// is reported as an exception of the tooling, and the registration still succeeds.
/// </para>
/// <para>
/// A license server URL, and a string that is not a license key, go to the primary product, which reports the error of
/// a string that it cannot register.
/// </para>
/// <para>
/// A license key held by several products is listed once, with the properties that the product to which it is native
/// reads. Since it is listed once, <see cref="RemoveLicense"/> removes it from the store of every product, and
/// <see cref="RemoveLicenses"/> removes the licenses of every product.
/// </para>
/// </remarks>
internal sealed class CompositeLicenseRegistrationService : ILicenseRegistrationService
{
    private readonly CompositeProductMembers _products;

    public CompositeLicenseRegistrationService( CompositeProductMembers products )
    {
        this._products = products;

        foreach ( var product in products.Members )
        {
            product.LicenseRegistrationService.PropertyChanged += this.OnPropertyChanged;
        }
    }

    private IEnumerable<ILicenseRegistrationService> Services => this._products.Members.Select( p => p.LicenseRegistrationService );

    private ILicenseRegistrationService Primary => this._products.Primary.LicenseRegistrationService;

    private void OnPropertyChanged( object? sender, PropertyChangedEventArgs e ) => this.PropertyChanged?.Invoke( this, e );

    private ILicenseRegistrationService GetService( string licenseString ) => this._products.GetMemberForLicense( licenseString ).LicenseRegistrationService;

    public LicenseRegistrationResult Register( SelfRegisteredEdition edition, SelfRegisteredEditionOptions? options = null )
    {
        // An edition is compared by identity, as the standard service does. An edition of no product goes to the primary
        // product, which reports that it is not one of its editions.
        var product = this._products.Members.FirstOrDefault( p => p.Catalog.SelfRegisteredEditions.Contains( edition ) ) ?? this._products.Primary;

        return product.LicenseRegistrationService.Register( edition, options );
    }

    public ImmutableArray<SelfRegisteredEdition> AvailableEditions => this.Services.SelectMany( s => s.AvailableEditions ).ToImmutableArray();

    public async ValueTask<LicenseRegistrationResult> RegisterLicenseAsync( string licenseString, CancellationToken cancellationToken = default )
    {
        var members = this._products.GetMembersAcceptingLicense( licenseString );
        var result = await members[0].LicenseRegistrationService.RegisterLicenseAsync( licenseString, cancellationToken );

        if ( !result.IsSuccess )
        {
            return result;
        }

        foreach ( var member in members.Skip( 1 ) )
        {
            try
            {
                var otherResult = await member.LicenseRegistrationService.RegisterLicenseAsync( licenseString, cancellationToken );

                if ( !otherResult.IsSuccess )
                {
                    throw new InvalidOperationException(
                        $"The license key could not be registered in the store of {member.Product.Profile.Name}: {otherResult.ErrorMessage}" );
                }
            }
            catch ( Exception e ) when ( e is not OperationCanceledException )
            {
                this.ReportStoreFailure( e );
            }
        }

        return result;
    }

    [Obsolete( "Use RegisterLicenseAsync." )]
    public LicenseRegistrationResult RegisterLicense( string licenseString )

        // See LicenseRegistrationService.Block for why the work is started on the thread pool.
        => Task.Run( () => this.RegisterLicenseAsync( licenseString ).AsTask() ).GetAwaiter().GetResult();

    /// <summary>
    /// Reports that a product other than the one to which a license key is native could not register it. The key is
    /// registered where it matters most, so the failure is reported and logged rather than thrown.
    /// </summary>
    private void ReportStoreFailure( Exception exception )
    {
        this._products.ServiceProvider.GetLoggerFactory().Licensing().Warning?.Log( $"A license key could not be registered in every store: {exception.Message}" );
        this._products.ServiceProvider.ReportToolingException( exception );
    }

    public void RemoveLicenses() => this.Services.ForEachContinueOnException( s => s.RemoveLicenses() );

    public void RemoveLicense( string licenseString ) => this.Services.ForEachContinueOnException( s => s.RemoveLicense( licenseString ) );

    public IEnumerable<LicenseRegistrationProperties> RegisteredLicenses
        => this._products.Members
            .SelectMany( m => m.LicenseRegistrationService.RegisteredLicenses.Select( l => (Member: m, License: l) ) )
            .GroupBy( x => x.License.LicenseString, StringComparer.Ordinal )
            .Select( g => g.FirstOrDefault( x => x.License.LicenseServerUrl == null && x.Member == this._products.GetMemberForProduct( x.License.Product ) ).License
                          ?? g.First().License );

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
