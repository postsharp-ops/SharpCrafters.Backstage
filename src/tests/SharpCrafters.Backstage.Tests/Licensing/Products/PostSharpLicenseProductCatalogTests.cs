// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using PostSharp.Backstage;
using PostSharp.Backstage.PostSharp;
using SharpCrafters.Backstage.Licensing;
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

        foreach ( var otherProduct in new[]
                  {
                      LicenseProduct.PostSharpUltimate, LicenseProduct.PostSharpFramework, LicenseProduct.PostSharpEssentials,
                      LicenseProduct.PostSharpCachingLibrary, LicenseProduct.PostSharpDiagnosticsLibrary, LicenseProduct.PostSharpModelLibrary,
                      LicenseProduct.PostSharpThreadingLibrary
                  } )
        {
            if ( otherProduct == product )
            {
                continue;
            }

            Assert.Contains( otherProduct, coexisting );
        }
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
    /// Every product is stored in the list, which is the <c>LicenseKeys</c> sub-key. The single slot holds one key,
    /// and the products of this family co-exist, so storing them there would drop every key but the last.
    /// </summary>
    [Theory]
    [InlineData( LicenseProduct.PostSharpUltimate )]
    [InlineData( LicenseProduct.PostSharpFramework )]
    [InlineData( LicenseProduct.PostSharpEssentials )]
    [InlineData( LicenseProduct.PostSharpCachingLibrary )]
    public void EveryProductIsStoredInTheLicenseList( LicenseProduct product )
        => Assert.True( _catalog.IsStoredInLicenseList( product ) );

    /// <summary>
    /// Registering a product replaces the key of that same product rather than adding a second one beside it, so a
    /// product never co-exists with itself.
    /// </summary>
    [Theory]
    [InlineData( LicenseProduct.PostSharpFramework )]
    [InlineData( LicenseProduct.PostSharpEssentials )]
    [InlineData( LicenseProduct.PostSharpCachingLibrary )]
    public void AProductDoesNotCoexistWithItself( LicenseProduct product )
        => Assert.DoesNotContain( product, _catalog.GetProductsCoexistingWith( product ) );

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

    [Fact]
    public void DisplayNamesComeFromTheSharedCatalog()
    {
        Assert.Equal( "PostSharp Ultimate", _catalog.GetDisplayName( LicenseProduct.PostSharpUltimate ) );
        Assert.Equal( "PostSharp Ultimate", _catalog.PremiumEditionDisplayName );
        Assert.Equal( "PostSharp Logging", _catalog.GetDisplayName( LicenseProduct.PostSharpDiagnosticsLibrary ) );
    }

#pragma warning restore CS0618
}
