// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Licensing;
using SharpCrafters.Backstage.Licensing.Licenses;
using SharpCrafters.Backstage.Licensing.Registration;
using System;
using System.Collections.Immutable;
using System.Linq;

namespace SharpCrafters.Backstage.Application;

/// <summary>
/// The union of the license product catalogs of the products of a composite product.
/// </summary>
/// <remarks>
/// A question about a license product is answered by the catalog that owns the product: the catalog to which the
/// product is native, or else the first catalog whose family includes it, or else the catalog of the primary product.
/// A question about the family as a whole is answered by the catalog of the primary product, except
/// <see cref="SelfRegisteredEditions"/>, which lists the editions of every catalog.
/// </remarks>
internal sealed class CompositeLicenseProductCatalog : ILicenseProductCatalog
{
    private readonly ImmutableArray<ILicenseProductCatalog> _catalogs;

    public CompositeLicenseProductCatalog( ImmutableArray<ILicenseProductCatalog> catalogs )
    {
        this._catalogs = catalogs;

        // Built once, because an edition is compared by identity.
        this.SelfRegisteredEditions = catalogs.SelectMany( c => c.SelfRegisteredEditions ).ToImmutableArray();
    }

    private ILicenseProductCatalog Primary => this._catalogs[0];

    private ILicenseProductCatalog GetOwner( LicenseProduct product )
        => this._catalogs.FirstOrDefault( c => c.IsNativeProduct( product ) )
           ?? this._catalogs.FirstOrDefault( c => c.IsProductOfFamily( product ) )
           ?? this.Primary;

    public string GetDisplayName( LicenseProduct product ) => this.GetOwner( product ).GetDisplayName( product );

    public string GetLicenseDisplayName( LicenseProduct product, LicenseType licenseType )
        => this.GetOwner( product ).GetLicenseDisplayName( product, licenseType );

    public ServicingPhase GetDefaultServicingPhase( LicenseProduct product ) => this.GetOwner( product ).GetDefaultServicingPhase( product );

    public bool CanHaveLongTermSupportOption( LicenseProduct product ) => this.GetOwner( product ).CanHaveLongTermSupportOption( product );

    public bool IsProductOfFamily( LicenseProduct product ) => this._catalogs.Any( c => c.IsProductOfFamily( product ) );

    public bool IsNativeProduct( LicenseProduct product ) => this._catalogs.Any( c => c.IsNativeProduct( product ) );

    public bool IsFreeLicense( LicenseProduct product, LicenseType licenseType ) => this.GetOwner( product ).IsFreeLicense( product, licenseType );

    public bool IsStoredInLicenseList( LicenseProduct product ) => this.GetOwner( product ).IsStoredInLicenseList( product );

    public ImmutableArray<LicenseProduct> GetProductsCoexistingWith( LicenseProduct product ) => this.GetOwner( product ).GetProductsCoexistingWith( product );

    public Version? GetMinimalVersion( LicenseKeyData licenseKeyData ) => this.GetOwner( licenseKeyData.Product ).GetMinimalVersion( licenseKeyData );

    public Version? MinimalLicenseServerVersion => this.Primary.MinimalLicenseServerVersion;

    public string PremiumEditionDisplayName => this.Primary.PremiumEditionDisplayName;

    public LicenseProduct EvaluationProduct => this.Primary.EvaluationProduct;

    public LicenseProduct? LicenseServerProduct => this.Primary.LicenseServerProduct;

    public bool HasUnlicensedEdition => this.Primary.HasUnlicensedEdition;

    public ImmutableArray<SelfRegisteredEdition> SelfRegisteredEditions { get; }
}
