// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using JetBrains.Annotations;
using SharpCrafters.Backstage.Extensibility;
using SharpCrafters.Backstage.Licensing.Registration;
using SharpCrafters.Backstage.Licensing.Licenses;
using System;
using System.Collections.Immutable;

namespace SharpCrafters.Backstage.Licensing;

/// <summary>
/// Describes the products that a product family sells and the business rules attached to them: display names, default
/// servicing phases, which products a license key of the family may name, and the editions that the family gives away.
/// The host product supplies the implementation, because these rules belong to the vendor and not to the licensing
/// services.
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
    /// Determines whether a registered license key of a product is stored in <see cref="LicensingConfiguration.Licenses"/>,
    /// which holds any number of keys, rather than in <see cref="LicensingConfiguration.LegacyLicense"/>, which holds one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The two slots differ in how many keys they hold and in which versions read them, and a family may choose the
    /// list for either reason. Metalama chooses it for the second: a key of a product that its earlier versions do
    /// not know has to stay out of the single slot those versions read. PostSharp chooses it for the first: its
    /// editions and pattern libraries are complementary, so a user holds several keys at once and one slot cannot
    /// hold them.
    /// </para>
    /// <para>
    /// Answering <see langword="false"/> for a family whose products co-exist loses keys, because each registration
    /// overwrites the single slot, and <see cref="GetProductsCoexistingWith"/> then keeps products that nothing can
    /// store.
    /// </para>
    /// </remarks>
    bool IsStoredInLicenseList( LicenseProduct product );

    /// <summary>
    /// Gets the products whose registered license keys are kept when a license key of a given product is registered.
    /// The keys of every other product are removed. The result is empty when no key is kept.
    /// </summary>
    ImmutableArray<LicenseProduct> GetProductsCoexistingWith( LicenseProduct product );

    /// <summary>
    /// Gets the earliest version of this family that can consume a license key, or <see langword="null"/> when every
    /// released version can consume it. Registration stores the key in the group named after that version, which the
    /// earlier versions do not read.
    /// </summary>
    /// <param name="licenseKeyData">The data of the license key.</param>
    /// <remarks>
    /// <para>
    /// The question is asked of the family, and not answered once for both, because the two families have released
    /// different readers and therefore differ on which keys an installed version can cope with. What a key needs of
    /// Metalama is <c>LicenseKeyDataExtensions.GetMinMetalamaVersion</c>, and of PostSharp
    /// <c>LicenseKeyDataExtensions.GetMinPostSharpVersion</c>.
    /// </para>
    /// <para>
    /// Answering a version is not free: the versions below it stop seeing the key at all, and a version that could
    /// have used it is then told that no license is registered. So a family answers <see langword="null"/> whenever
    /// its released versions can all cope, and names a version only for a key that would otherwise reach a reader
    /// which reports it as invalid.
    /// </para>
    /// </remarks>
    Version? GetMinimalVersion( LicenseKeyData licenseKeyData );

    /// <summary>
    /// Gets the earliest version of this family that understands a registered license server, or
    /// <see langword="null"/> when every released version understands one. A registered license server URL is stored
    /// in the group of that version.
    /// </summary>
    /// <remarks>
    /// This is asked instead of <see cref="GetMinimalVersion"/> for a license server, because what is registered is a
    /// URL and not a license key: it has no content to judge, and the licence it leases today is not the one it will
    /// lease tomorrow. It is a property of the family for the same reason as <see cref="GetMinimalVersion"/>, and the
    /// two families answer differently: PostSharp has had license servers since before it recorded which version was
    /// asking, and Metalama has them from the version that introduces them.
    /// </remarks>
    Version? MinimalLicenseServerVersion { get; }

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
    /// Gets the editions that a user can obtain by asking for them rather than by buying them, in the order in which
    /// they are offered.
    /// </summary>
    /// <remarks>
    /// This is what the command line and the setup pages present. Each edition says where it is offered, so a family
    /// that has no free edition declares none and neither surface offers one. Every family offers a trial, so the list
    /// always holds at least that.
    /// </remarks>
    ImmutableArray<SelfRegisteredEdition> SelfRegisteredEditions { get; }
}
