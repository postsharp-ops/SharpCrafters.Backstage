// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using JetBrains.Annotations;
using SharpCrafters.Backstage.Extensibility;
using SharpCrafters.Backstage.Licensing.Registration;
using System;
using System.Collections.Immutable;

namespace SharpCrafters.Backstage.Licensing;

/// <summary>
/// Describes the products that a product family sells and the business rules attached to them: display names, default
/// servicing phases, which products a license key of the family may name, and which products the unsigned licenses
/// (community, evaluation) are issued for. The host product supplies the implementation, because these rules belong
/// to the vendor and not to the licensing services.
/// </summary>
/// <remarks>
/// The wire format of a license key identifies a product by a byte, represented by <see cref="LicenseProduct"/>. The
/// catalog gives that byte its meaning for the current product family.
/// </remarks>
[PublicAPI]
public interface ILicenseProductCatalog : IBackstageService
{
    /// <summary>
    /// Gets the display name of a product, for instance <c>Metalama Professional</c>.
    /// </summary>
    string GetDisplayName( LicenseProduct product );

    /// <summary>
    /// Gets the display name of a license key of a given product and type, for instance
    /// <c>Metalama Professional, Business License</c>.
    /// </summary>
    string GetLicenseDisplayName( LicenseProduct product, LicenseType licenseType );

    /// <summary>
    /// Gets the servicing phase that a license key of a product qualifies for when the key does not specify one.
    /// </summary>
    ServicingPhase GetDefaultServicingPhase( LicenseProduct product );

    /// <summary>
    /// Determines whether a product can be sold with a long-term support option.
    /// </summary>
    bool CanHaveLongTermSupportOption( LicenseProduct product );

    /// <summary>
    /// Determines whether a license key of a given product can be consumed by the current product family at all. A
    /// key of another product is rejected with a message before any requirement is evaluated.
    /// </summary>
    bool IsProductOfFamily( LicenseProduct product );

    /// <summary>
    /// Determines whether a license is a free edition, which has no subscription to renew.
    /// </summary>
    /// <remarks>
    /// The license type is read as well as the product, because a family may express the free edition through it:
    /// the free edition of PostSharp is a PostSharp Ultimate key carrying the Community type, and the product alone
    /// would not tell it from a key the user has paid for.
    /// </remarks>
    bool IsFreeLicense( LicenseProduct product, LicenseType licenseType );

    /// <summary>
    /// Determines whether a registered license key of a product must be stored in the group of keys that only the
    /// versions supporting that product read, rather than in the legacy location that every version reads.
    /// </summary>
    bool RequiresVersionSpecificRegistration( LicenseProduct product );

    /// <summary>
    /// Gets the products whose registered license keys are kept when a license key of a given product is registered.
    /// The keys of every other product are removed. The result is empty when no key is kept.
    /// </summary>
    ImmutableArray<LicenseProduct> GetProductsCoexistingWith( LicenseProduct product );

    /// <summary>
    /// Gets the display name of the edition that the user is invited to try or to buy when a component is not
    /// licensed, for instance <c>Metalama Professional</c>.
    /// </summary>
    string PremiumEditionDisplayName { get; }

    /// <summary>
    /// Gets the product for which an evaluation license is issued.
    /// </summary>
    LicenseProduct EvaluationProduct { get; }

    /// <summary>
    /// Gets the product that a license server is asked to allocate a lease from, which names the pool of licences on
    /// the server, or <c>null</c> to let the server choose among every pool it holds.
    /// </summary>
    /// <remarks>
    /// One license server can hold the licences of several products, so naming the product lets it allocate from the
    /// right pool. A server that predates this argument ignores it and allocates from any pool, which is what the
    /// clients of PostSharp, which never sent it, rely on.
    /// </remarks>
    LicenseProduct? LicenseServerProduct { get; }

    /// <summary>
    /// Gets a value indicating whether the product can be used without registering anything at all, which is what
    /// the setup pages offer as staying with the open source edition.
    /// </summary>
    /// <remarks>
    /// This is not the same as having a free edition. Metalama has both: it runs unlicensed with a reduced feature
    /// set, and it also issues a Community key that unlocks more. PostSharp has the second and not the first, so a
    /// user who registers nothing can build nothing.
    /// </remarks>
    bool HasUnlicensedEdition { get; }

    /// <summary>
    /// Describes the trial license of the family.
    /// </summary>
    /// <param name="utcNow">The current moment.</param>
    UnsignedLicense CreateTrialLicense( DateTime utcNow );

    /// <summary>
    /// Gets the editions that a user can obtain by asking for them rather than by buying them, in the order in which
    /// they are offered. The result is empty when the family offers none.
    /// </summary>
    /// <remarks>
    /// This is what the command line and the setup pages present. A family that has no free edition declares none,
    /// and neither of them offers one.
    /// </remarks>
    ImmutableArray<SelfRegisteredEdition> SelfRegisteredEditions { get; }

    /// <summary>
    /// Describes the free license that a user registers without buying anything, or returns <see langword="null"/>
    /// when the family offers none.
    /// </summary>
    /// <param name="utcNow">The current moment.</param>
    /// <remarks>
    /// Returning <see langword="null"/> is how a family says that the edition does not exist, and it is what the
    /// setup pages and the command line read to decide whether to offer it.
    /// </remarks>
    UnsignedLicense? CreateFreeLicense( DateTime utcNow );

    /// <summary>
    /// Describes the free license that earlier versions of the product issued, or returns <see langword="null"/>
    /// when the family never had one.
    /// </summary>
    /// <param name="utcNow">The current moment.</param>
    UnsignedLicense? CreateLegacyFreeLicense( DateTime utcNow );
}
