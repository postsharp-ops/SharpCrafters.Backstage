// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using PostSharp.Backstage;
using SharpCrafters.Backstage.Licensing;
using Xunit;

namespace SharpCrafters.Backstage.Tests.Licensing.Products;

/// <summary>
/// Pins what a PostSharp license grants. PostSharp 2026.0 computes the same table from the same keys, and a key that
/// grants one thing in one version and another in the other is a licence the user cannot reason about.
/// </summary>
/// <remarks>
/// The numbers of the other version are pinned once, on the packages themselves, because those numbers are the
/// meaning of the license keys this version consumes and nothing else in the code says what they are. Everything
/// after that is written in packages rather than in numbers: what a set contains, and which set a license maps to,
/// are readable claims that happen to add up to those numbers.
/// </remarks>
public sealed class PostSharpLicensedPackagesTests
{
#pragma warning disable CS0618 // Type or member is obsolete: the mapping must name the products that are no longer offered.

    /// <summary>
    /// Each package is the bit that PostSharp 2026.0 gives it. This is the one place where the numbers appear, and
    /// they must not change: a license key carries a set of these bits, so moving one changes what every key already
    /// issued grants.
    /// </summary>
    [Theory]
    [InlineData( 0, LicensedPackages.None )]
    [InlineData( 1, LicensedPackages.Essentials )]
    [InlineData( 2, LicensedPackages.Common )]
    [InlineData( 4, LicensedPackages.Framework )]
    [InlineData( 8, LicensedPackages.Threading )]
    [InlineData( 16, LicensedPackages.Model )]
    [InlineData( 32, LicensedPackages.Xaml )]
    [InlineData( 64, LicensedPackages.Aggregatable )]
    [InlineData( 128, LicensedPackages.Diagnostics )]
    [InlineData( 256, LicensedPackages.Caching )]
    public void APackageIsTheBitOfPostSharp2026( int expected, LicensedPackages package ) => Assert.Equal( expected, (int) package );

    /// <summary>
    /// Each set contains the packages that PostSharp 2026.0 puts in it.
    /// </summary>
    /// <remarks>
    /// Written as the packages rather than as the number they add up to, so that the claim can be read. The numbers
    /// are still pinned, by <see cref="APackageIsTheBitOfPostSharp2026"/>: a package that moved would change the
    /// number of every set that contains it, and be caught there.
    /// </remarks>
    [Theory]
    [InlineData( LicensedPackageSets.Essentials, LicensedPackages.Essentials | LicensedPackages.Common )]
    [InlineData(
        LicensedPackageSets.Mvvm,
        LicensedPackages.Essentials | LicensedPackages.Common | LicensedPackages.Model | LicensedPackages.Xaml | LicensedPackages.Aggregatable )]
    [InlineData(
        LicensedPackageSets.Threading,
        LicensedPackages.Essentials | LicensedPackages.Common | LicensedPackages.Threading | LicensedPackages.Aggregatable )]
    [InlineData( LicensedPackageSets.Logging, LicensedPackages.Essentials | LicensedPackages.Common | LicensedPackages.Diagnostics )]
    [InlineData( LicensedPackageSets.Caching, LicensedPackages.Essentials | LicensedPackages.Common | LicensedPackages.Caching )]
    [InlineData(
        LicensedPackageSets.Framework,
        LicensedPackages.Essentials | LicensedPackages.Common | LicensedPackages.Framework | LicensedPackages.Diagnostics )]
    [InlineData( LicensedPackageSets.Ultimate, LicensedPackages.All )]
    [InlineData( LicensedPackageSets.Unattended, LicensedPackages.All & ~LicensedPackages.Diagnostics )]
    public void ASetContainsThePackagesOfPostSharp2026( LicensedPackages set, LicensedPackages expected ) => Assert.Equal( expected, set );

    /// <summary>
    /// Every package is in <see cref="LicensedPackages.All"/>, so a set that grants everything grants each of them.
    /// </summary>
    [Theory]
    [InlineData( LicensedPackages.Essentials )]
    [InlineData( LicensedPackages.Common )]
    [InlineData( LicensedPackages.Framework )]
    [InlineData( LicensedPackages.Threading )]
    [InlineData( LicensedPackages.Model )]
    [InlineData( LicensedPackages.Xaml )]
    [InlineData( LicensedPackages.Aggregatable )]
    [InlineData( LicensedPackages.Diagnostics )]
    [InlineData( LicensedPackages.Caching )]
    public void EverythingContainsEveryPackage( LicensedPackages package ) => Assert.True( LicensedPackages.All.Includes( package ) );

    /// <summary>
    /// A license maps to the set that PostSharp 2026.0 maps it to. What each set contains is settled by
    /// <see cref="ASetContainsThePackagesOfPostSharp2026"/>; what is under test here is which one a license gets.
    /// </summary>
    [Theory]

    // PostSharp Ultimate grants everything, whatever the commercial form of the key.
    [InlineData( LicenseProduct.PostSharpUltimate, LicenseType.Business, LicensedPackageSets.Ultimate )]
    [InlineData( LicenseProduct.PostSharpUltimate, LicenseType.Personal, LicensedPackageSets.Ultimate )]
    [InlineData( LicenseProduct.PostSharpUltimate, LicenseType.Site, LicensedPackageSets.Ultimate )]
    [InlineData( LicenseProduct.PostSharpUltimate, LicenseType.Global, LicensedPackageSets.Ultimate )]
    [InlineData( LicenseProduct.PostSharpUltimate, LicenseType.Enterprise, LicensedPackageSets.Ultimate )]
    [InlineData( LicenseProduct.PostSharpUltimate, LicenseType.Academic, LicensedPackageSets.Ultimate )]
    [InlineData( LicenseProduct.PostSharpUltimate, LicenseType.Evaluation, LicensedPackageSets.Ultimate )]

    // Except when it carries the Community type, which is how the free edition is registered.
    [InlineData( LicenseProduct.PostSharpUltimate, LicenseType.Community, LicensedPackageSets.Essentials )]

    // PostSharp Framework grants the framework and logging, and no other pattern library.
    [InlineData( LicenseProduct.PostSharpFramework, LicenseType.Business, LicensedPackageSets.Framework )]
    [InlineData( LicenseProduct.PostSharpFramework, LicenseType.Evaluation, LicensedPackageSets.Framework )]
    [InlineData( LicenseProduct.PostSharpFramework, LicenseType.Community, LicensedPackageSets.Framework )]

    // A pattern library grants itself and the free edition. The license type does not enter into it.
    [InlineData( LicenseProduct.PostSharpDiagnosticsLibrary, LicenseType.Business, LicensedPackageSets.Logging )]
    [InlineData( LicenseProduct.PostSharpModelLibrary, LicenseType.Business, LicensedPackageSets.Mvvm )]
    [InlineData( LicenseProduct.PostSharpThreadingLibrary, LicenseType.Business, LicensedPackageSets.Threading )]
    [InlineData( LicenseProduct.PostSharpCachingLibrary, LicenseType.Business, LicensedPackageSets.Caching )]
    [InlineData( LicenseProduct.PostSharpCachingLibrary, LicenseType.Site, LicensedPackageSets.Caching )]

    // The free edition, as LicenseKeyDataExtensions.NormalizeProduct names it once the key is consumed.
    [InlineData( LicenseProduct.PostSharpEssentials, LicenseType.Community, LicensedPackageSets.Essentials )]
    [InlineData( LicenseProduct.PostSharpEssentials, LicenseType.Business, LicensedPackageSets.Essentials )]

    // A product that this version does not place grants the free edition, never nothing.
    [InlineData( LicenseProduct.PostSharpUltimate1, LicenseType.Business, LicensedPackageSets.Essentials )]
    [InlineData( LicenseProduct.PostSharp20, LicenseType.Business, LicensedPackageSets.Essentials )]
    [InlineData( LicenseProduct.None, LicenseType.Business, LicensedPackageSets.Essentials )]

    // An unmodified build, which the version control system vouches for, is entitled to everything.
    [InlineData( LicenseProduct.PostSharpFramework, LicenseType.Unmodified, LicensedPackageSets.Ultimate )]

    // An unattended build is entitled to everything but logging, which is licensed per production server rather than
    // per build. The license type decides this, whatever product the key names.
    [InlineData( LicenseProduct.PostSharpUltimate, LicenseType.Unattended, LicensedPackageSets.Unattended )]
    [InlineData( LicenseProduct.PostSharpFramework, LicenseType.Unattended, LicensedPackageSets.Unattended )]
    [InlineData( LicenseProduct.PostSharpCachingLibrary, LicenseType.Unattended, LicensedPackageSets.Unattended )]
    public void ALicenseGrantsTheSetOfPostSharp2026( LicenseProduct product, LicenseType licenseType, LicensedPackages expected )
        => Assert.Equal( expected, PostSharpLicenseExtensions.GetLicensedPackages( product, licenseType ) );

    /// <summary>
    /// An unattended build gets everything but logging, said as what it may and may not do rather than as the name of
    /// the set it is given.
    /// </summary>
    [Fact]
    public void AnUnattendedBuildIsEntitledToEverythingButLogging()
    {
        var granted = PostSharpLicenseExtensions.GetLicensedPackages( LicenseProduct.PostSharpUltimate, LicenseType.Unattended );

        Assert.False( granted.Includes( LicensedPackages.Diagnostics ) );
        Assert.True( granted.Includes( LicensedPackages.Framework ) );
        Assert.True( granted.Includes( LicensedPackages.Caching ) );
        Assert.True( granted.Includes( LicensedPackages.Threading ) );
    }

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
    [Theory]
    [InlineData( LicensedPackageSets.Framework, LicensedPackages.Framework, true )]
    [InlineData( LicensedPackageSets.Framework, LicensedPackages.Diagnostics, true )]
    [InlineData( LicensedPackageSets.Framework, LicensedPackages.Threading, false )]
    [InlineData( LicensedPackageSets.Framework, LicensedPackages.Caching, false )]
    [InlineData( LicensedPackageSets.Threading, LicensedPackages.Aggregatable, true )]
    [InlineData( LicensedPackageSets.Threading, LicensedPackages.Model, false )]

    // A requirement of several packages is satisfied only when every one of them is granted.
    [InlineData( LicensedPackageSets.Framework, LicensedPackages.Framework | LicensedPackages.Diagnostics, true )]
    [InlineData( LicensedPackageSets.Framework, LicensedPackages.Framework | LicensedPackages.Caching, false )]

    // The empty requirement is satisfied by anything, including by nothing.
    [InlineData( LicensedPackageSets.Essentials, LicensedPackages.None, true )]
    [InlineData( LicensedPackages.None, LicensedPackages.None, true )]
    public void ASetSatisfiesARequirementItContains( LicensedPackages granted, LicensedPackages requirement, bool isSatisfied )
        => Assert.Equal( isSatisfied, granted.Includes( requirement ) );

#pragma warning restore CS0618
}
