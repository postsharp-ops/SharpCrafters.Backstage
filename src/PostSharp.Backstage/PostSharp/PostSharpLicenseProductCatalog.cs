// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using JetBrains.Annotations;
using SharpCrafters.Backstage.Licensing;
using System.Collections.Immutable;

namespace PostSharp.Backstage;

/// <summary>
/// The catalog of the products of PostSharp Technologies as consumed by the PostSharp product family: the PostSharp
/// editions and the PostSharp pattern libraries. Metalama license keys are not consumed by PostSharp.
/// </summary>
[PublicAPI]
public sealed class PostSharpLicenseProductCatalog : LicenseProductCatalog
{
    /// <summary>
    /// Gets the single instance of the catalog.
    /// </summary>
    public static PostSharpLicenseProductCatalog Instance { get; } = new();

    private PostSharpLicenseProductCatalog() { }

    /// <summary>
    /// The products of the PostSharp family, in the order in which they are presented.
    /// </summary>
#pragma warning disable CS0618 // Type or member is obsolete: the catalog must name the products that are no longer offered.
    private static readonly ImmutableArray<LicenseProduct> _familyProducts = ImmutableArray.Create(
        LicenseProduct.PostSharpUltimate,
        LicenseProduct.PostSharpFramework,
        LicenseProduct.PostSharpEssentials,
        LicenseProduct.PostSharpDiagnosticsLibrary,
        LicenseProduct.PostSharpModelLibrary,
        LicenseProduct.PostSharpThreadingLibrary,
        LicenseProduct.PostSharpCachingLibrary,

        // No longer issued but existing keys are fully supported.
        LicenseProduct.PostSharp20,
        LicenseProduct.PostSharpUltimate1 );

    /// <inheritdoc />
    public override bool IsProductOfFamily( LicenseProduct product ) => _familyProducts.Contains( product );

    /// <inheritdoc />
    /// <remarks>
    /// PostSharp Essentials is the free edition, but a key of it carries <see cref="LicenseProduct.PostSharpUltimate"/>
    /// with <see cref="LicenseType.Community"/>: the edition is expressed by the license type and not by the product.
    /// No PostSharp product is therefore free on its own.
    /// </remarks>
    public override bool IsFreeProduct( LicenseProduct product ) => false;

    /// <inheritdoc />
    /// <remarks>
    /// PostSharp gates a license key on the <c>MinPostSharpVersion</c> field of the key itself, never on its product,
    /// so no product of this family requires a version-specific registration.
    /// </remarks>
    public override bool RequiresVersionSpecificRegistration( LicenseProduct product ) => false;

    /// <inheritdoc />
    /// <remarks>
    /// Unlike Metalama, where co-existence is a backward-compatibility exception, it is the norm in PostSharp: the
    /// editions and the pattern libraries are complementary and a user may hold several at once. The exception is
    /// PostSharp Ultimate, which covers everything the others do and therefore replaces them all.
    /// </remarks>
    public override ImmutableArray<LicenseProduct> GetProductsCoexistingWith( LicenseProduct product )
        => product == LicenseProduct.PostSharpUltimate || product == LicenseProduct.PostSharpUltimate1
            ? ImmutableArray<LicenseProduct>.Empty
            : _familyProducts;
#pragma warning restore CS0618

    /// <inheritdoc />
    public override string PremiumEditionDisplayName => "PostSharp Ultimate";

    /// <inheritdoc />
    /// <remarks>
    /// PostSharp 2026.0 generates the trial key as PostSharp Ultimate with <see cref="LicenseType.Evaluation"/>, and
    /// this value is what keeps a trial started in 2027 readable by it.
    /// </remarks>
    public override LicenseProduct EvaluationProduct => LicenseProduct.PostSharpUltimate;

    /// <inheritdoc />
    /// <remarks>
    /// PostSharp has no community edition in the sense Metalama has: its free edition is PostSharp Essentials, which
    /// is registered as a license type rather than as a product of its own.
    /// </remarks>
    public override LicenseProduct? CommunityProduct => null;

    /// <inheritdoc />
    public override LicenseProduct? LegacyFreeProduct => null;
}
