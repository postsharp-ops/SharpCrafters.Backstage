// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using JetBrains.Annotations;
using SharpCrafters.Backstage.Licensing.Licenses;
using SharpCrafters.Backstage.Licensing.Registration;
using System;
using System.Collections.Immutable;
using System.Threading;

namespace SharpCrafters.Backstage.Licensing;

/// <summary>
/// The catalog of the products of PostSharp Technologies: the members that are common to the Metalama and PostSharp
/// product families, such as the display names of every product. The members that depend on the product family,
/// such as the edition that a trial gives, are implemented by the derived class of each family.
/// </summary>
/// <remarks>
/// This class is defined in the licensing package because <see cref="LicenseProduct"/>, the enumeration of the
/// products that a license key can name, is defined there too.
/// </remarks>
[PublicAPI]
public abstract class LicenseProductCatalog : ILicenseProductCatalog
{
#pragma warning disable CS0618 // Type or member is obsolete: the catalog must name the products that are no longer offered.

    /// <inheritdoc />
    public virtual string GetDisplayName( LicenseProduct product )
        => product switch
        {
            LicenseProduct.MetalamaCommunity => "Metalama Community",
            LicenseProduct.MetalamaEnterprise => "Metalama Enterprise",
            LicenseProduct.MetalamaProfessional => "Metalama Professional",
            LicenseProduct.PostSharpEssentials => "PostSharp Essentials",
            LicenseProduct.PostSharpFramework => "PostSharp Framework",
            LicenseProduct.PostSharpUltimate => "PostSharp Ultimate",
            LicenseProduct.PostSharpCachingLibrary => "PostSharp Caching",
            LicenseProduct.PostSharpDiagnosticsLibrary => "PostSharp Logging",
            LicenseProduct.PostSharpModelLibrary => "PostSharp MVVM",
            LicenseProduct.PostSharpThreadingLibrary => "PostSharp Threading",
            LicenseProduct.MetalamaFree => "Metalama Free (legacy)",
            LicenseProduct.MetalamaStarter => "Metalama Starter (legacy)",
            LicenseProduct.MetalamaUltimate => "Metalama Ultimate (legacy)",
            _ => product.ToString()
        };

    /// <inheritdoc />
    public virtual string GetLicenseDisplayName( LicenseProduct product, LicenseType licenseType )
        => product switch
        {
            LicenseProduct.MetalamaProfessional => $"Metalama Professional, {licenseType.GetLicenseTypeName()}",
            LicenseProduct.MetalamaUltimate => $"Metalama Ultimate, {licenseType.GetLicenseTypeName()}",
            LicenseProduct.MetalamaStarter => $"Metalama Starter, {licenseType.GetLicenseTypeName()}",
            _ => this.GetDisplayName( product )
        };

    /// <inheritdoc />
    public virtual ServicingPhase GetDefaultServicingPhase( LicenseProduct product )
        => product switch
        {
            // Metalama Enterprise is Metalama Professional with a ServicingPhase field set to LongTerm.
            LicenseProduct.MetalamaProfessional => ServicingPhase.Extended,
            LicenseProduct.MetalamaEnterprise => ServicingPhase.LongTerm,
            LicenseProduct.PostSharpFramework => ServicingPhase.Extended,
            LicenseProduct.PostSharpUltimate => ServicingPhase.Extended,
            _ => ServicingPhase.Current
        };

    /// <inheritdoc />
    public virtual bool CanHaveLongTermSupportOption( LicenseProduct product )
        => product switch
        {
            LicenseProduct.PostSharpUltimate => true,
            LicenseProduct.PostSharpFramework => true,
            _ => false
        };

    /// <inheritdoc />
    public abstract bool IsProductOfFamily( LicenseProduct product );

    /// <inheritdoc />
    public abstract bool IsFreeLicense( LicenseProduct product, LicenseType licenseType );

    /// <inheritdoc />
    public abstract bool IsStoredInLicenseList( LicenseProduct product );

    /// <inheritdoc />
    public abstract ImmutableArray<LicenseProduct> GetProductsCoexistingWith( LicenseProduct product );

    /// <inheritdoc />
    /// <remarks>
    /// A family that does not override this answers that every one of its released versions can consume every key, so
    /// no key is ever put out of their reach. That is the right answer until the family releases a reader which
    /// refuses something a later key can carry.
    /// </remarks>
    public virtual Version? GetMinimalVersion( LicenseKeyData licenseKeyData ) => null;

    /// <inheritdoc />
    /// <remarks>
    /// The default is the version of this package that introduces license servers, which is the right answer for a
    /// family whose license servers arrive with it. A family that had them before overrides this.
    /// </remarks>
    public virtual Version? MinimalLicenseServerVersion => LicensingConstants.MinimalLicenseServerVersion;

    /// <inheritdoc />
    public abstract string PremiumEditionDisplayName { get; }

    /// <inheritdoc />
    public abstract LicenseProduct EvaluationProduct { get; }

    /// <inheritdoc />
    /// <remarks>
    /// The default is <see cref="EvaluationProduct"/>, which is the premium product of the family and therefore the
    /// pool that a license server of this product family holds.
    /// </remarks>
    public virtual LicenseProduct? LicenseServerProduct => this.EvaluationProduct;

    /// <inheritdoc />
    public abstract bool HasUnlicensedEdition { get; }

    /// <inheritdoc />
    /// <remarks>
    /// Each family lists its own, trial included, and assigns the list in its constructor so that it is built once.
    /// It has to be the same objects every time: an edition is compared by identity — it is the object a caller
    /// passes back to <c>ILicenseRegistrationService.Register</c>, which checks that it belongs to this family — so a
    /// second list of equivalent editions is not equivalent at all, and a caller holding one from the first would be
    /// told that it is not an edition of this product.
    /// </remarks>
    public abstract ImmutableArray<SelfRegisteredEdition> SelfRegisteredEditions { get; }

#pragma warning restore CS0618
}
