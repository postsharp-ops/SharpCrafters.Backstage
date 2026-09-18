// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using PostSharp.Backstage;
using SharpCrafters.Backstage.Licensing;
using System;
using Xunit;

namespace SharpCrafters.Backstage.Tests.Licensing.Products;

/// <summary>
/// Tests the rules by which the PostSharp product family accepts and stores license keys. They differ from the
/// Metalama ones in every member that the catalog leaves to the family, so each is pinned here rather than being
/// left to the reader of the catalog.
/// </summary>
public sealed class PostSharpLicenseProductCatalogTests
{
#pragma warning disable CS0618 // Type or member is obsolete: the catalog must name the products that are no longer offered.

    private static readonly ILicenseProductCatalog _catalog = PostSharpLicenseProductCatalog.Instance;

    [Theory]
    [InlineData( LicenseProduct.PostSharpUltimate )]
    [InlineData( LicenseProduct.PostSharpFramework )]
    [InlineData( LicenseProduct.PostSharpEssentials )]
    [InlineData( LicenseProduct.PostSharpCachingLibrary )]
    [InlineData( LicenseProduct.PostSharpDiagnosticsLibrary )]
    [InlineData( LicenseProduct.PostSharpModelLibrary )]
    [InlineData( LicenseProduct.PostSharpThreadingLibrary )]
    [InlineData( LicenseProduct.PostSharp20 )]
    [InlineData( LicenseProduct.PostSharpUltimate1 )]
    public void PostSharpProductsAreOfTheFamily( LicenseProduct product ) => Assert.True( _catalog.IsProductOfFamily( product ) );

    /// <summary>
    /// PostSharp accepts PostSharp license keys only. This is the opposite of Metalama, which accepts PostSharp
    /// Framework and Ultimate keys as well.
    /// </summary>
    [Theory]
    [InlineData( LicenseProduct.MetalamaCommunity )]
    [InlineData( LicenseProduct.MetalamaProfessional )]
    [InlineData( LicenseProduct.MetalamaEnterprise )]
    [InlineData( LicenseProduct.MetalamaUltimate )]
    [InlineData( LicenseProduct.MetalamaStarter )]
    [InlineData( LicenseProduct.MetalamaFree )]
    [InlineData( LicenseProduct.None )]
    public void MetalamaProductsAreNotOfTheFamily( LicenseProduct product ) => Assert.False( _catalog.IsProductOfFamily( product ) );

    /// <summary>
    /// The editions and the pattern libraries of PostSharp are complementary, so registering one keeps the others.
    /// </summary>
    [Theory]
    [InlineData( LicenseProduct.PostSharpFramework )]
    [InlineData( LicenseProduct.PostSharpEssentials )]
    [InlineData( LicenseProduct.PostSharpCachingLibrary )]
    [InlineData( LicenseProduct.PostSharpDiagnosticsLibrary )]
    [InlineData( LicenseProduct.PostSharpModelLibrary )]
    [InlineData( LicenseProduct.PostSharpThreadingLibrary )]
    public void EveryProductButUltimateCoexistsWithTheWholeFamily( LicenseProduct product )
    {
        var coexisting = _catalog.GetProductsCoexistingWith( product );

        Assert.Contains( LicenseProduct.PostSharpUltimate, coexisting );
        Assert.Contains( LicenseProduct.PostSharpFramework, coexisting );
        Assert.Contains( LicenseProduct.PostSharpCachingLibrary, coexisting );
    }

    /// <summary>
    /// PostSharp Ultimate covers what every other product of the family covers, so registering it replaces them all.
    /// An empty result is what makes <c>LicensingConfiguration.SetLicense</c> remove the other keys.
    /// </summary>
    [Theory]
    [InlineData( LicenseProduct.PostSharpUltimate )]
    [InlineData( LicenseProduct.PostSharpUltimate1 )]
    public void UltimateReplacesEveryOtherLicense( LicenseProduct product ) => Assert.Empty( _catalog.GetProductsCoexistingWith( product ) );

    /// <summary>
    /// The free edition of PostSharp is expressed by <see cref="LicenseType.Community"/> on a PostSharp Ultimate key,
    /// so it is the license type that makes a license free and not the product: the same product, bought, is not.
    /// </summary>
    [Theory]
    [InlineData( LicenseProduct.PostSharpUltimate, LicenseType.Community, true )]
    [InlineData( LicenseProduct.PostSharpEssentials, LicenseType.Community, true )]
    [InlineData( LicenseProduct.PostSharpEssentials, LicenseType.Business, true )]
    [InlineData( LicenseProduct.PostSharpUltimate, LicenseType.Business, false )]
    [InlineData( LicenseProduct.PostSharpUltimate, LicenseType.Site, false )]
    [InlineData( LicenseProduct.PostSharpFramework, LicenseType.Business, false )]
    public void OnlyACommunityLicenseIsFree( LicenseProduct product, LicenseType licenseType, bool expected )
        => Assert.Equal( expected, _catalog.IsFreeLicense( product, licenseType ) );

    /// <summary>
    /// PostSharp gates a key on its own <c>MinPostSharpVersion</c> field, so a key is never stored in a
    /// version-specific bucket because of its product.
    /// </summary>
    [Theory]
    [InlineData( LicenseProduct.PostSharpUltimate )]
    [InlineData( LicenseProduct.PostSharpFramework )]
    [InlineData( LicenseProduct.PostSharpEssentials )]
    public void NoProductRequiresVersionSpecificRegistration( LicenseProduct product )
        => Assert.False( _catalog.RequiresVersionSpecificRegistration( product ) );

    /// <summary>
    /// PostSharp 2026.0 generates the trial key as PostSharp Ultimate, and the two versions share the registry, so a
    /// trial started by either must name the same product.
    /// </summary>
    [Fact]
    public void TrialIsUltimate() => Assert.Equal( LicenseProduct.PostSharpUltimate, _catalog.EvaluationProduct );

    /// <summary>
    /// PostSharp does nothing without a license, so the setup pages must not offer to stay unlicensed. This is what
    /// keeps the page from offering an open source edition that PostSharp does not have.
    /// </summary>
    [Fact]
    public void PostSharpDoesNothingWithoutALicense() => Assert.False( _catalog.HasUnlicensedEdition );

    /// <summary>
    /// The free edition is a PostSharp Ultimate key carrying the Community type, which is the key that PostSharp
    /// 2026.0 generates, and the two versions share the registered keys. It does not expire.
    /// </summary>
    [Fact]
    public void TheFreeEditionIsAnUltimateKeyOfTheCommunityType()
    {
        var freeLicense = _catalog.CreateFreeLicense( new DateTime( 2026, 9, 18, 12, 0, 0, DateTimeKind.Utc ) );

        Assert.NotNull( freeLicense );
        Assert.Equal( LicenseProduct.PostSharpUltimate, freeLicense.Product );
        Assert.Equal( LicenseType.Community, freeLicense.LicenseType );
        Assert.Null( freeLicense.ValidTo );
    }

    /// <summary>
    /// PostSharp never issued the legacy free edition that Metalama 2025.0 and earlier did, so the command that
    /// registers it is not offered.
    /// </summary>
    [Fact]
    public void ThereIsNoLegacyFreeEdition() => Assert.Null( _catalog.CreateLegacyFreeLicense( DateTime.UtcNow ) );

    /// <summary>
    /// The trial is PostSharp Ultimate for the period every family gives, and it carries a subscription that ends
    /// with it, so that a build made with a version released later is not covered by it.
    /// </summary>
    [Fact]
    public void TheTrialIsUltimateForFortyFiveDays()
    {
        var trial = _catalog.CreateTrialLicense( new DateTime( 2026, 9, 18, 22, 30, 0, DateTimeKind.Utc ) );

        Assert.Equal( LicenseProduct.PostSharpUltimate, trial.Product );
        Assert.Equal( LicenseType.Evaluation, trial.LicenseType );

        // Counted from midnight, so that a trial started late in the evening is not a day shorter.
        Assert.Equal( new DateTime( 2026, 9, 18 ), trial.ValidFrom );
        Assert.Equal( new DateTime( 2026, 9, 18 ).AddDays( 45 ), trial.ValidTo );
        Assert.Equal( trial.ValidTo, trial.SubscriptionEndDate );
    }

    [Fact]
    public void DisplayNamesComeFromTheSharedCatalog()
    {
        Assert.Equal( "PostSharp Ultimate", _catalog.GetDisplayName( LicenseProduct.PostSharpUltimate ) );
        Assert.Equal( "PostSharp Ultimate", _catalog.PremiumEditionDisplayName );
        Assert.Equal( "PostSharp Logging", _catalog.GetDisplayName( LicenseProduct.PostSharpDiagnosticsLibrary ) );
    }

#pragma warning restore CS0618
}
