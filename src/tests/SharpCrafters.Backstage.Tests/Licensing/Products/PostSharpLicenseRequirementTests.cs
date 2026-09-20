// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using PostSharp.Backstage;
using SharpCrafters.Backstage.Licensing.Consumption;
using SharpCrafters.Backstage.Licensing.Consumption.Requirements;
using SharpCrafters.Backstage.Testing;
using SharpCrafters.Backstage.Tests.Licensing.Consumption;
using System;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace SharpCrafters.Backstage.Tests.Licensing.Products;

/// <summary>
/// Drives the PostSharp requirements through the whole consumption service with real license keys, rather than over
/// the mapping alone.
/// </summary>
/// <remarks>
/// A PostSharp key entitles some features of a build and not others, which no Metalama key does, so the cases that
/// matter are the ones where a valid key is refused: a pattern library key against another library, and the free
/// edition against anything above it.
/// </remarks>
public sealed class PostSharpLicenseRequirementTests : LicenseConsumptionServiceTestsBase
{
    // The catalog of the product family decides which keys are consumable at all, so these tests run under the
    // PostSharp product: under Metalama, a key of a PostSharp pattern library is refused before any requirement is
    // consulted.
    public PostSharpLicenseRequirementTests( ITestOutputHelper logger ) : base( logger, product: PostSharpProduct.Instance ) { }

    private async Task AssertConsumesAsync( string licenseKeyName, LicenseRequirement requirement, bool expected )
    {
        this.SetBuildDate( LicenseKeyProvider.DefaultSubscriptionExpirationDate.AddDays( -1 ) );

        var licenseKey = LicenseKeyProvider.GetLicenseKey( licenseKeyName );
        var license = this.CreateInstrumentedLicenseWrapper( licenseKey );
        var consumer = await this.CreateConsumptionService( license ).CreateConsumerAsync();

        Assert.Equal( expected, consumer.TryConsume( requirement ) );
    }

    /// <summary>
    /// Every valid key, including the free edition and a single pattern library, satisfies the requirement that a
    /// project is checked against once.
    /// </summary>
    [Theory]
    [InlineData( nameof(TestLicenseKeyProvider.PostSharpEssentials) )]
    [InlineData( nameof(TestLicenseKeyProvider.PostSharpFramework) )]
    [InlineData( nameof(TestLicenseKeyProvider.PostSharpUltimate) )]
    [InlineData( nameof(TestLicenseKeyProvider.PostSharpLogging) )]
    [InlineData( nameof(TestLicenseKeyProvider.PostSharpCaching) )]
    public Task EveryKeySatisfiesTheFreeEdition( string licenseKeyName )
        => this.AssertConsumesAsync( licenseKeyName, PostSharpLicenseRequirements.Essentials, true );

    /// <summary>
    /// An aspect written by the user needs PostSharp Framework or PostSharp Ultimate. The free edition does not
    /// entitle it, and neither does a pattern library key.
    /// </summary>
    [Theory]
    [InlineData( nameof(TestLicenseKeyProvider.PostSharpFramework), true )]
    [InlineData( nameof(TestLicenseKeyProvider.PostSharpUltimate), true )]
    [InlineData( nameof(TestLicenseKeyProvider.PostSharpEssentials), false )]
    [InlineData( nameof(TestLicenseKeyProvider.PostSharpLogging), false )]
    [InlineData( nameof(TestLicenseKeyProvider.PostSharpMvvm), false )]
    [InlineData( nameof(TestLicenseKeyProvider.PostSharpThreading), false )]
    [InlineData( nameof(TestLicenseKeyProvider.PostSharpCaching), false )]
    public Task AnAspectOfTheUserNeedsTheFramework( string licenseKeyName, bool expected )
        => this.AssertConsumesAsync( licenseKeyName, PostSharpLicenseRequirements.Framework, expected );

    /// <summary>
    /// PostSharp Framework includes logging, which is the one pattern library it grants.
    /// </summary>
    [Theory]
    [InlineData( nameof(TestLicenseKeyProvider.PostSharpLogging), true )]
    [InlineData( nameof(TestLicenseKeyProvider.PostSharpFramework), true )]
    [InlineData( nameof(TestLicenseKeyProvider.PostSharpUltimate), true )]
    [InlineData( nameof(TestLicenseKeyProvider.PostSharpEssentials), false )]
    [InlineData( nameof(TestLicenseKeyProvider.PostSharpCaching), false )]
    public Task LoggingComesWithTheFrameworkAndWithItsOwnLibrary( string licenseKeyName, bool expected )
        => this.AssertConsumesAsync( licenseKeyName, PostSharpLicenseRequirements.Diagnostics, expected );

    /// <summary>
    /// The other pattern libraries come only with their own key or with PostSharp Ultimate, and in particular not
    /// with PostSharp Framework.
    /// </summary>
    [Theory]
    [InlineData( nameof(TestLicenseKeyProvider.PostSharpCaching), true )]
    [InlineData( nameof(TestLicenseKeyProvider.PostSharpUltimate), true )]
    [InlineData( nameof(TestLicenseKeyProvider.PostSharpFramework), false )]
    [InlineData( nameof(TestLicenseKeyProvider.PostSharpLogging), false )]
    [InlineData( nameof(TestLicenseKeyProvider.PostSharpEssentials), false )]
    public Task CachingComesOnlyWithItsOwnLibraryOrUltimate( string licenseKeyName, bool expected )
        => this.AssertConsumesAsync( licenseKeyName, PostSharpLicenseRequirements.Caching, expected );

    [Theory]
    [InlineData( nameof(TestLicenseKeyProvider.PostSharpThreading), true )]
    [InlineData( nameof(TestLicenseKeyProvider.PostSharpUltimate), true )]
    [InlineData( nameof(TestLicenseKeyProvider.PostSharpFramework), false )]
    [InlineData( nameof(TestLicenseKeyProvider.PostSharpMvvm), false )]
    public Task ThreadingComesOnlyWithItsOwnLibraryOrUltimate( string licenseKeyName, bool expected )
        => this.AssertConsumesAsync( licenseKeyName, PostSharpLicenseRequirements.Threading, expected );

    /// <summary>
    /// Aggregation comes with the threading library as well as with the MVVM one, because both rest on it.
    /// </summary>
    [Theory]
    [InlineData( nameof(TestLicenseKeyProvider.PostSharpThreading), true )]
    [InlineData( nameof(TestLicenseKeyProvider.PostSharpMvvm), true )]
    [InlineData( nameof(TestLicenseKeyProvider.PostSharpUltimate), true )]
    [InlineData( nameof(TestLicenseKeyProvider.PostSharpFramework), false )]
    [InlineData( nameof(TestLicenseKeyProvider.PostSharpCaching), false )]
    public Task AggregationComesWithThreadingAndWithMvvm( string licenseKeyName, bool expected )
        => this.AssertConsumesAsync( licenseKeyName, PostSharpLicenseRequirements.Aggregatable, expected );

    /// <summary>
    /// The MVVM library grants XAML as well as the model, which is the one key that grants two packages a user may
    /// think of as separate products.
    /// </summary>
    [Theory]
    [InlineData( nameof(TestLicenseKeyProvider.PostSharpMvvm), true )]
    [InlineData( nameof(TestLicenseKeyProvider.PostSharpUltimate), true )]
    [InlineData( nameof(TestLicenseKeyProvider.PostSharpFramework), false )]
    public Task XamlComesWithMvvm( string licenseKeyName, bool expected )
        => this.AssertConsumesAsync( licenseKeyName, PostSharpLicenseRequirements.Xaml, expected );

    /// <summary>
    /// A Metalama key is not a PostSharp key, whatever the feature. This is the asymmetry of the two families: a
    /// PostSharp Framework or Ultimate key is valid for Metalama, and no Metalama key is valid for PostSharp.
    /// </summary>
    [Theory]
    [InlineData( nameof(TestLicenseKeyProvider.MetalamaProfessionalBusiness) )]
    [InlineData( nameof(TestLicenseKeyProvider.MetalamaEnterprise) )]
    [InlineData( nameof(TestLicenseKeyProvider.MetalamaCommunity) )]
    public async Task AMetalamaKeyEntitlesNothingInPostSharp( string licenseKeyName )
    {
        await this.AssertConsumesAsync( licenseKeyName, PostSharpLicenseRequirements.Framework, false );
        await this.AssertConsumesAsync( licenseKeyName, PostSharpLicenseRequirements.Essentials, false );
    }

    /// <summary>
    /// The Visual Studio extension needs a paid edition. The free edition does not entitle it, although its key is a
    /// PostSharp Ultimate key and PostSharp Ultimate does.
    /// </summary>
    /// <remarks>
    /// This is what the requirement reads: the product as written in the key, or the product the key stands for. The
    /// free edition is the only case where the two differ, so it is the only case that tells them apart.
    /// </remarks>
    [Theory]
    [InlineData( nameof(TestLicenseKeyProvider.PostSharpEssentials), false )]
    [InlineData( nameof(TestLicenseKeyProvider.PostSharpLogging), false )]
    [InlineData( nameof(TestLicenseKeyProvider.PostSharpFramework), true )]
    [InlineData( nameof(TestLicenseKeyProvider.PostSharpUltimate), true )]
    public Task TheVisualStudioToolsNeedAPaidEdition( string licenseKeyName, bool expected )
        => this.AssertConsumesAsync( licenseKeyName, new ToolingLicenseRequirement( PostSharpProduct.Profile ), expected );

    /// <summary>
    /// A key that does not validate entitles nothing, so that a broken key is not a way past the check.
    /// </summary>
    [Theory]
    [InlineData( nameof(TestLicenseKeyProvider.ExpiredSubscription) )]
    [InlineData( nameof(TestLicenseKeyProvider.PostSharpUltimateUnsigned) )]
    public Task AnInvalidKeyEntitlesNothing( string licenseKeyName )
        => this.AssertConsumesAsync( licenseKeyName, PostSharpLicenseRequirements.Essentials, false );

    /// <summary>
    /// A diagnostic tells the user which products grant the package they are missing. The free edition is not among
    /// them, because it is not a product a user can buy.
    /// </summary>
    [Fact]
    public void ADiagnosticNamesTheProductsThatGrantThePackage()
    {
        var catalog = PostSharpLicenseProductCatalog.Instance;

        Assert.Equal(
            ["PostSharp Ultimate", "PostSharp Caching"],
            PostSharpLicenseRequirements.Caching.GetEligibleProductNames( catalog ) );

        Assert.Equal(
            ["PostSharp Ultimate", "PostSharp Framework", "PostSharp Logging"],
            PostSharpLicenseRequirements.Diagnostics.GetEligibleProductNames( catalog ) );

        // Aggregation is granted by two libraries as well as by Ultimate.
        Assert.Equal(
            ["PostSharp Ultimate", "PostSharp MVVM", "PostSharp Threading"],
            PostSharpLicenseRequirements.Aggregatable.GetEligibleProductNames( catalog ) );
    }

    /// <summary>
    /// Every package has a requirement, and asking for one twice gives one object. The compiler asks by the value of
    /// the enumeration, because that is what it works out from the assembly that declares an aspect.
    /// </summary>
    /// <remarks>
    /// The cases are taken from the enumeration rather than listed here, so that a package added to the enumeration
    /// and not to the mapping fails this test instead of failing the first build that uses it.
    /// </remarks>
    [Fact]
    public void EveryPackageHasARequirement()
    {
        foreach ( LicensedPackages package in Enum.GetValues( typeof(LicensedPackages) ) )
        {
            if ( package is LicensedPackages.None or LicensedPackages.All )
            {
                continue;
            }

            var requirement = PostSharpLicenseRequirements.ForPackage( package );

            Assert.Equal( package, Assert.IsType<PostSharpLicenseRequirement>( requirement ).RequiredPackage );
            Assert.Same( requirement, PostSharpLicenseRequirements.ForPackage( package ) );
        }
    }

    /// <summary>
    /// A requirement names exactly one package, so asking for none of them, for all of them, or for a combination is
    /// refused rather than answered with one of them.
    /// </summary>
    [Theory]
    [InlineData( LicensedPackages.None )]
    [InlineData( LicensedPackages.All )]
    [InlineData( LicensedPackages.Model | LicensedPackages.Xaml )]
    public void AskingForSomethingOtherThanOnePackageIsRefused( LicensedPackages packages )
        => Assert.Throws<ArgumentOutOfRangeException>( () => PostSharpLicenseRequirements.ForPackage( packages ) );
}
