// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using PostSharp.Backstage;
using SharpCrafters.Backstage.Licensing;
using Xunit;

namespace SharpCrafters.Backstage.Tests.Licensing.Products;

/// <summary>
/// Pins what a PostSharp license grants. PostSharp 2026.0 computes the same table from the same keys, and a key that
/// grants one thing in one version and another in the other is a licence the user cannot reason about, so every
/// expected value here is the numeric one of <c>LicensedProductPackages</c> in that version rather than a
/// restatement of the code under test.
/// </summary>
public sealed class PostSharpLicensedPackagesTests
{
#pragma warning disable CS0618 // Type or member is obsolete: the mapping must name the products that are no longer offered.

    /// <summary>
    /// The numeric values of the sets, as PostSharp 2026.0 computes them. They are spelled out so that a change to
    /// the flags is caught here rather than in a build of the other version.
    /// </summary>
    [Fact]
    public void TheSetsHaveTheValuesOfPostSharp2026()
    {
        Assert.Equal( 3, (int) LicensedPackageSets.Essentials );
        Assert.Equal( 115, (int) LicensedPackageSets.Mvvm );
        Assert.Equal( 75, (int) LicensedPackageSets.Threading );
        Assert.Equal( 131, (int) LicensedPackageSets.Logging );
        Assert.Equal( 259, (int) LicensedPackageSets.Caching );
        Assert.Equal( 135, (int) LicensedPackageSets.Framework );
        Assert.Equal( 511, (int) LicensedPackageSets.Ultimate );
        Assert.Equal( 383, (int) LicensedPackageSets.Unattended );
        Assert.Equal( 511, (int) LicensedPackages.All );
    }

    [Theory]

    // PostSharp Ultimate grants everything, whatever the commercial form of the key.
    [InlineData( LicenseProduct.PostSharpUltimate, LicenseType.Business, 511 )]
    [InlineData( LicenseProduct.PostSharpUltimate, LicenseType.Personal, 511 )]
    [InlineData( LicenseProduct.PostSharpUltimate, LicenseType.Site, 511 )]
    [InlineData( LicenseProduct.PostSharpUltimate, LicenseType.Global, 511 )]
    [InlineData( LicenseProduct.PostSharpUltimate, LicenseType.Enterprise, 511 )]
    [InlineData( LicenseProduct.PostSharpUltimate, LicenseType.Academic, 511 )]
    [InlineData( LicenseProduct.PostSharpUltimate, LicenseType.Evaluation, 511 )]

    // Except when it carries the Community type, which is how the free edition is registered.
    [InlineData( LicenseProduct.PostSharpUltimate, LicenseType.Community, 3 )]

    // PostSharp Framework grants the framework and logging, and no other pattern library.
    [InlineData( LicenseProduct.PostSharpFramework, LicenseType.Business, 135 )]
    [InlineData( LicenseProduct.PostSharpFramework, LicenseType.Evaluation, 135 )]
    [InlineData( LicenseProduct.PostSharpFramework, LicenseType.Community, 135 )]

    // A pattern library grants itself and the free edition. The license type does not enter into it.
    [InlineData( LicenseProduct.PostSharpDiagnosticsLibrary, LicenseType.Business, 131 )]
    [InlineData( LicenseProduct.PostSharpModelLibrary, LicenseType.Business, 115 )]
    [InlineData( LicenseProduct.PostSharpThreadingLibrary, LicenseType.Business, 75 )]
    [InlineData( LicenseProduct.PostSharpCachingLibrary, LicenseType.Business, 259 )]
    [InlineData( LicenseProduct.PostSharpCachingLibrary, LicenseType.Site, 259 )]

    // The free edition, as LicenseKeyDataExtensions.NormalizeProduct names it once the key is consumed.
    [InlineData( LicenseProduct.PostSharpEssentials, LicenseType.Community, 3 )]
    [InlineData( LicenseProduct.PostSharpEssentials, LicenseType.Business, 3 )]

    // A product that this version does not place grants the free edition, never nothing.
    [InlineData( LicenseProduct.PostSharpUltimate1, LicenseType.Business, 3 )]
    [InlineData( LicenseProduct.PostSharp20, LicenseType.Business, 3 )]
    [InlineData( LicenseProduct.None, LicenseType.Business, 3 )]
    public void AProductGrantsThePackagesOfPostSharp2026( LicenseProduct product, LicenseType licenseType, int expected )
        => Assert.Equal( expected, (int) PostSharpLicenseExtensions.GetLicensedPackages( product, licenseType ) );

    /// <summary>
    /// An unattended build is entitled to everything but logging, which is licensed per production server rather
    /// than per build. The license type decides this, whatever product the key names.
    /// </summary>
    [Theory]
    [InlineData( LicenseProduct.PostSharpUltimate )]
    [InlineData( LicenseProduct.PostSharpFramework )]
    [InlineData( LicenseProduct.PostSharpCachingLibrary )]
    public void AnUnattendedBuildIsEntitledToEverythingButLogging( LicenseProduct product )
    {
        var granted = PostSharpLicenseExtensions.GetLicensedPackages( product, LicenseType.Unattended );

        Assert.Equal( 383, (int) granted );
        Assert.False( granted.Includes( LicensedPackages.Diagnostics ) );
        Assert.True( granted.Includes( LicensedPackages.Framework ) );
        Assert.True( granted.Includes( LicensedPackages.Caching ) );
    }

    /// <summary>
    /// A build of sources that the version control system reports as unmodified is entitled to everything.
    /// </summary>
    [Fact]
    public void AnUnmodifiedBuildIsEntitledToEverything()
        => Assert.Equal(
            511,
            (int) PostSharpLicenseExtensions.GetLicensedPackages( LicenseProduct.PostSharpFramework, LicenseType.Unmodified ) );

    /// <summary>
    /// Every valid license grants the free edition, which is what lets a project be checked against it once.
    /// </summary>
    [Theory]
    [InlineData( LicenseProduct.PostSharpUltimate, LicenseType.Community )]
    [InlineData( LicenseProduct.PostSharpFramework, LicenseType.Business )]
    [InlineData( LicenseProduct.PostSharpCachingLibrary, LicenseType.Business )]
    [InlineData( LicenseProduct.None, LicenseType.Business )]
    public void EveryLicenseGrantsTheFreeEdition( LicenseProduct product, LicenseType licenseType )
        => Assert.True( PostSharpLicenseExtensions.GetLicensedPackages( product, licenseType ).Includes( LicensedPackages.Essentials ) );

    /// <summary>
    /// A set satisfies a requirement when it contains every package of it, which is how one key entitles some
    /// features of a build and not others.
    /// </summary>
    [Fact]
    public void ASetSatisfiesARequirementItContains()
    {
        Assert.True( LicensedPackageSets.Framework.Includes( LicensedPackages.Framework ) );
        Assert.True( LicensedPackageSets.Framework.Includes( LicensedPackages.Diagnostics ) );
        Assert.False( LicensedPackageSets.Framework.Includes( LicensedPackages.Threading ) );
        Assert.False( LicensedPackageSets.Framework.Includes( LicensedPackages.Caching ) );

        Assert.True( LicensedPackageSets.Threading.Includes( LicensedPackages.Aggregatable ) );
        Assert.False( LicensedPackageSets.Threading.Includes( LicensedPackages.Model ) );

        // The empty requirement is satisfied by anything, including by nothing.
        Assert.True( LicensedPackages.None.Includes( LicensedPackages.None ) );
    }

#pragma warning restore CS0618
}
