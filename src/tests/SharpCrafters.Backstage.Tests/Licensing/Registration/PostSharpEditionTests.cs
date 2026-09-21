// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using PostSharp.Backstage;
using PostSharp.Backstage.PostSharp;
using SharpCrafters.Backstage.Licensing;
using SharpCrafters.Backstage.Licensing.Registration;
using System;
using System.Linq;
using Xunit;
using Xunit.Abstractions;

namespace SharpCrafters.Backstage.Tests.Licensing.Registration;

/// <summary>
/// Tests what the editions of the PostSharp family grant, and where they are offered.
/// </summary>
/// <remarks>
/// The licenses are asked of the editions rather than read back from a registration, because registering normalizes
/// the product: what the key says and what a registered license reports are deliberately different for this family,
/// and it is what the key says that PostSharp 2026.0 reads.
/// </remarks>
public sealed class PostSharpEditionTests : LicensingTestsBase
{
    public PostSharpEditionTests( ITestOutputHelper logger )
        : base( logger, product: PostSharpProduct.Instance, version: PostSharpVersion ) { }

    /// <summary>
    /// The free edition is a PostSharp Ultimate key carrying the Community type, which is the key that PostSharp
    /// 2026.0 generates, and the two versions share the registered keys. It does not expire.
    /// </summary>
    [Fact]
    public void TheFreeEditionIsAnUltimateKeyOfTheCommunityType()
    {
        var license = this.GetEdition( SelfRegisteredEditionKind.Free ).CreateLicense( this.CreateEditionContext() );

        Assert.Equal( LicenseProduct.PostSharpUltimate, license.Product );
        Assert.Equal( LicenseType.Community, license.LicenseType );
        Assert.Null( license.ValidTo );
    }

    /// <summary>
    /// The free edition names itself after the edition and not after the product its key carries, so that a user
    /// whose trial is ending is invited to the edition that costs nothing and not to PostSharp Ultimate.
    /// </summary>
    [Fact]
    public void TheFreeEditionNamesItselfEssentials()
        => Assert.Equal( "PostSharp Essentials", this.GetEdition( SelfRegisteredEditionKind.Free ).DisplayName );

    /// <summary>
    /// PostSharp never issued the legacy free edition that Metalama 2025.0 and earlier did.
    /// </summary>
    [Fact]
    public void ThereIsNoLegacyFreeEdition()
        => Assert.DoesNotContain( SelfRegisteredEditionKind.LegacyFree, this.Catalog.SelfRegisteredEditions.Select( e => e.Kind ) );

    /// <summary>
    /// The trial is PostSharp Ultimate for the period every family gives, and it carries a subscription that ends
    /// with it, so that a build made with a version released later is not covered by it.
    /// </summary>
    [Fact]
    public void TheTrialIsUltimateForFortyFiveDays()
    {
        this.Time.Set( new DateTime( 2026, 9, 18, 22, 30, 0, DateTimeKind.Utc ) );

        var trial = this.GetEdition( SelfRegisteredEditionKind.Trial ).CreateLicense( this.CreateEditionContext() );

        Assert.Equal( LicenseProduct.PostSharpUltimate, trial.Product );
        Assert.Equal( LicenseType.Evaluation, trial.LicenseType );

        // Counted from midnight, so that a trial started late in the evening is not a day shorter.
        Assert.Equal( new DateTime( 2026, 9, 18 ), trial.ValidFrom );
        Assert.Equal( new DateTime( 2026, 9, 18 ).AddDays( 45 ), trial.ValidTo );
        Assert.Equal( trial.ValidTo, trial.SubscriptionEndDate );
    }

    /// <summary>
    /// Both editions are offered from the command line, and both are offered during setup: PostSharp does nothing
    /// without a license, so a user setting it up must be able to reach one from the page.
    /// </summary>
    [Fact]
    public void BothEditionsAreOfferedEverywhere()
    {
        Assert.All( this.Catalog.SelfRegisteredEditions, e => Assert.True( e.IsAvailableFromCommandLine ) );
        Assert.All( this.Catalog.SelfRegisteredEditions, e => Assert.NotNull( e.SetupTitle ) );
    }

    /// <summary>
    /// The trial comes after the edition that costs nothing, which is the order the setup pages present.
    /// </summary>
    [Fact]
    public void TheTrialIsOfferedLast()
        => Assert.Equal( SelfRegisteredEditionKind.Trial, this.Catalog.SelfRegisteredEditions.Last().Kind );
}
