// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using JetBrains.Annotations;
using SharpCrafters.Backstage.Licensing;
using SharpCrafters.Backstage.Licensing.Licenses;
using SharpCrafters.Backstage.Licensing.Registration;
using System;
using System.Collections.Immutable;

namespace PostSharp.Backstage.Licensing;

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

    private PostSharpLicenseProductCatalog()
    {
        // Assigned here rather than from a property initializer, because an edition is given the catalog and a
        // property initializer has no 'this' to give it. An edition only stores the catalog, so one that is still
        // being constructed is no trouble.
        this.SelfRegisteredEditions = [new PostSharpEssentialsEdition(), new TrialEdition( this )];
    }

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
    /// The free edition of PostSharp is a PostSharp Ultimate key carrying <see cref="LicenseType.Community"/>, so it
    /// is the license type that makes it free and not the product. A key normalized to
    /// <see cref="LicenseProduct.PostSharpEssentials"/> is the same edition under the name it is given once it has
    /// been read.
    /// </remarks>
    public override bool IsFreeLicense( LicenseProduct product, LicenseType licenseType )
        => licenseType == LicenseType.Community || product == LicenseProduct.PostSharpEssentials;

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// Every product of this family is stored in the list, for two reasons. The first is that the products co-exist:
    /// a user holds an edition and the pattern libraries that complement it, and the single slot holds one key, so
    /// registering the second would drop the first.
    /// </para>
    /// <para>
    /// The second is where the other version writes. The list is the <c>LicenseKeys</c> sub-key, which is where
    /// PostSharp 2026.0 registers its own keys and which it calls the standard location. The single slot is the root
    /// <c>LicenseKey</c> value, which it calls the location compatible with PostSharp 3.0: it reads that value, so a
    /// key written there does work, but it only ever deletes it and never writes it. Registering into a value the
    /// other version treats as a leftover to clean up would leave the two versions disagreeing about where a license
    /// belongs.
    /// </para>
    /// </remarks>
    public override bool IsStoredInLicenseList( LicenseProduct product ) => true;

    /// <inheritdoc />
    /// <remarks>
    /// Unlike Metalama, where co-existence is a backward-compatibility exception, it is the norm in PostSharp: the
    /// editions and the pattern libraries are complementary and a user may hold several at once. The exception is
    /// PostSharp Ultimate, which covers everything the others do and therefore replaces them all.
    /// </remarks>
    /// <returns>
    /// The other products whose keys survive, which never includes <paramref name="product"/> itself: registering a
    /// product replaces the key of that same product rather than adding a second one beside it.
    /// </returns>
    public override ImmutableArray<LicenseProduct> GetProductsCoexistingWith( LicenseProduct product )
        => product == LicenseProduct.PostSharpUltimate || product == LicenseProduct.PostSharpUltimate1
            ? ImmutableArray<LicenseProduct>.Empty
            : _familyProducts.Remove( product );
#pragma warning restore CS0618

    /// <summary>
    /// The version from which PostSharp keeps a license key out of the reach of the earlier versions, which is the
    /// rule that PostSharp 2026.0 applies under the name <c>License.RequiresVersionSpecificStore</c>.
    /// </summary>
    private static readonly Version _firstVersionWithVersionSpecificStore = new( 5, 0, 0 );

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// PostSharp is installed side by side, version by version, and every installed version reads the same registry
    /// key. A key of the current generation carries fields that the readers before 6.5.17, 6.8.10 and 6.9.3 refuse,
    /// so leaving it where they look makes them report a license the user has paid for as invalid.
    /// </para>
    /// <para>
    /// The threshold is the one PostSharp 2026.0 uses, so that a key registered here lands where a key registered
    /// there lands. Below it the answer is <see langword="null"/>: those keys are consumed by every version ever
    /// released, and naming a version would hide them from the versions that want them.
    /// </para>
    /// </remarks>
    public override Version? GetMinimalVersion( LicenseKeyData licenseKeyData )
    {
        var minimalVersion = licenseKeyData.GetMinPostSharpVersion();

        return minimalVersion >= _firstVersionWithVersionSpecificStore ? minimalVersion : null;
    }

    /// <inheritdoc />
    /// <remarks>
    /// No version at all. PostSharp has had license servers for long enough that no version anyone still runs is
    /// without them, so there is no floor worth naming, and naming one would hide a user's license server from the
    /// versions that want it. PostSharp 2026.0 stores the URL as a plain value beside the license keys, and this
    /// keeps it there.
    /// </remarks>
    public override Version? MinimalLicenseServerVersion => null;

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
    /// PostSharp does nothing without a license. A user who registers nothing can build nothing, so the setup pages
    /// must not offer to stay unlicensed.
    /// </remarks>
    public override bool HasUnlicensedEdition => false;

    /// <inheritdoc />
    /// <remarks>
    /// PostSharp gives away its Essentials edition, to everyone and asking nothing in return. The trial comes last,
    /// so that the setup pages offer the edition that costs nothing before the one that expires.
    /// </remarks>
    public override ImmutableArray<SelfRegisteredEdition> SelfRegisteredEditions { get; }
}
