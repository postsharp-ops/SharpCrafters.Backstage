// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using JetBrains.Annotations;
using SharpCrafters.Backstage.Licensing;
using SharpCrafters.Backstage.Licensing.Registration;
using System;
using System.Collections.Immutable;

namespace Metalama.Backstage;

/// <summary>
/// The catalog of the products of PostSharp Technologies as consumed by the Metalama product family: the Metalama
/// editions, and the PostSharp editions whose license keys are also valid for Metalama.
/// </summary>
[PublicAPI]
public sealed class MetalamaLicenseProductCatalog : LicenseProductCatalog
{
    /// <summary>
    /// Gets the single instance of the catalog.
    /// </summary>
    public static MetalamaLicenseProductCatalog Instance { get; } = new();

    private MetalamaLicenseProductCatalog() { }

#pragma warning disable CS0618 // Type or member is obsolete: the catalog must name the products that are no longer offered.

    /// <inheritdoc />
    public override string GetLicenseDisplayName( LicenseProduct product, LicenseType licenseType )
        => product == LicenseProduct.None ? "Metalama Open Source" : base.GetLicenseDisplayName( product, licenseType );

    /// <inheritdoc />
    public override bool IsProductOfFamily( LicenseProduct product )
        => product switch
        {
            LicenseProduct.MetalamaCommunity => true,
            LicenseProduct.MetalamaProfessional => true,
            LicenseProduct.MetalamaEnterprise => true,
            LicenseProduct.PostSharpFramework => true,
            LicenseProduct.PostSharpUltimate => true,

            // No longer issued but existing keys are fully supported.
            LicenseProduct.MetalamaUltimate => true,
            LicenseProduct.MetalamaStarter => true,
            LicenseProduct.MetalamaFree => true,
            _ => false
        };

    /// <inheritdoc />
    /// <remarks>
    /// Metalama expresses the free edition through the product, so the license type is not read.
    /// </remarks>
    public override bool IsFreeLicense( LicenseProduct product, LicenseType licenseType )
        => product is LicenseProduct.MetalamaCommunity or LicenseProduct.MetalamaFree;

    /// <inheritdoc />
    /// <remarks>
    /// Metalama Community was introduced in Metalama 2025.1, so its keys are stored where earlier versions do not read.
    /// </remarks>
    public override bool RequiresVersionSpecificRegistration( LicenseProduct product ) => product is LicenseProduct.MetalamaCommunity;

    /// <inheritdoc />
    /// <remarks>
    /// Metalama Community and Metalama Free co-exist for backward compatibility: a version that supports only one of
    /// them keeps consuming its own key.
    /// </remarks>
    public override ImmutableArray<LicenseProduct> GetProductsCoexistingWith( LicenseProduct product )
        => product switch
        {
            LicenseProduct.MetalamaCommunity => ImmutableArray.Create( LicenseProduct.MetalamaFree ),
            LicenseProduct.MetalamaFree => ImmutableArray.Create( LicenseProduct.MetalamaCommunity ),
            _ => ImmutableArray<LicenseProduct>.Empty
        };

    /// <inheritdoc />
    public override string PremiumEditionDisplayName => "Metalama Professional";

    /// <inheritdoc />
    public override LicenseProduct EvaluationProduct => LicenseProduct.MetalamaProfessional;

    /// <inheritdoc />
    /// <remarks>
    /// Metalama runs without a license, with the feature set of the open source edition.
    /// </remarks>
    public override bool HasUnlicensedEdition => true;

    /// <inheritdoc />
    /// <remarks>
    /// Metalama offers its Community edition, and still registers the free edition that its earlier versions issued.
    /// The second is not offered during setup: only a version that is being replaced can consume it.
    /// </remarks>
    public override ImmutableArray<SelfRegisteredEdition> SelfRegisteredEditions { get; } = ImmutableArray.Create(
        new SelfRegisteredEdition(
            "community",
            "Switches to the Metalama Community edition, which is free and must be renewed every year.",
            ( service, reason ) => service.RegisterCommunityEdition( reason ) )
        {
            SuccessMessage = "You are now using Metalama Community for Metalama 2025.1 and later.",

            // Not offered during setup: the setup pages have no way to ask why the user is entitled to it, and the
            // page already offers the open source edition, which needs no answer.
            RequiresReason = true
        },
#pragma warning disable CS0612 // Type or member is obsolete
        new SelfRegisteredEdition(
            "free",
            "Registers the Metalama Free license (for Metalama 2025.0 and earlier).",
            ( service, _ ) => service.RegisterLegacyFreeEdition() )
        {
            SuccessMessage = "You are now using Metalama Free for Metalama 2025.0 and earlier."
        } );
#pragma warning restore CS0612

    /// <inheritdoc />
    /// <remarks>
    /// Metalama Community must be renewed yearly, which is what limits an edition given away for nothing.
    /// </remarks>
    public override UnsignedLicense? CreateFreeLicense( DateTime utcNow )
        => new( LicenseProduct.MetalamaCommunity, LicenseType.Community, utcNow ) { ValidTo = utcNow.AddYears( 1 ) };

    /// <inheritdoc />
    /// <remarks>
    /// Metalama Free is the edition that Metalama 2025.0 and earlier issued. It never expires, because a version
    /// that reads it has no way to renew it.
    /// </remarks>
    public override UnsignedLicense? CreateLegacyFreeLicense( DateTime utcNow )
        => new( LicenseProduct.MetalamaFree, LicenseType.Community, utcNow );

#pragma warning restore CS0618
}
