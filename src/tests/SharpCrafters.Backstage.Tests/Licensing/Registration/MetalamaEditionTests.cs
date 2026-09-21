// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using PostSharp.Backstage.PostSharp;
using SharpCrafters.Backstage.Configuration;
using SharpCrafters.Backstage.Licensing;
using SharpCrafters.Backstage.Licensing.Registration;
using System;
using System.Linq;
using Xunit;
using Xunit.Abstractions;

namespace SharpCrafters.Backstage.Tests.Licensing.Registration;

/// <summary>
/// Tests what the editions of the Metalama family grant, and where they are offered.
/// </summary>
#pragma warning disable CS0618 // Type or member is obsolete: the legacy edition names a product no longer offered.

public sealed class MetalamaEditionTests : LicensingTestsBase
{
    public MetalamaEditionTests( ITestOutputHelper logger ) : base( logger ) { }

    /// <summary>
    /// Metalama Community must be renewed yearly, which is what limits an edition given away for nothing.
    /// </summary>
    [Fact]
    public void TheCommunityEditionExpiresAfterAYear()
    {
        this.Time.Set( new DateTime( 2026, 9, 18, 12, 0, 0, DateTimeKind.Utc ) );

        var license = this.GetEdition( SelfRegisteredEditionKind.Free ).CreateLicense( this.CreateEditionContext() );

        Assert.Equal( LicenseProduct.MetalamaCommunity, license.Product );
        Assert.Equal( LicenseType.Community, license.LicenseType );
        Assert.Equal( new DateTime( 2027, 9, 18, 12, 0, 0, DateTimeKind.Utc ), license.ValidTo );
    }

    /// <summary>
    /// The Community edition is installed by the Visual Studio extension, which asks the question that entitles the
    /// user to it. Nobody registers it by hand, so the command line advertises no verb for it.
    /// </summary>
    [Fact]
    public void TheCommunityEditionIsNotOfferedFromTheCommandLine()
    {
        var edition = this.GetEdition( SelfRegisteredEditionKind.Free );

        Assert.False( edition.IsAvailableFromCommandLine );

        // Nor during setup, which has no way to ask the question.
        Assert.Null( edition.SetupTitle );
    }

    /// <summary>
    /// The edition is given on conditions, so it is refused rather than registered when nothing says why the user is
    /// entitled to it.
    /// </summary>
    [Fact]
    public void TheCommunityEditionIsRefusedWithoutAReason()
    {
        var result = this.RegisterEdition( SelfRegisteredEditionKind.Free );

        Assert.False( result.IsSuccess );
        Assert.Empty( this.LicenseRegistrationService.RegisteredLicenses );
    }

    /// <summary>
    /// The reason is recorded beside the key, in the same transaction.
    /// </summary>
    [Fact]
    public void TheCommunityEditionRecordsTheReason()
    {
        Assert.True( this.RegisterEdition( SelfRegisteredEditionKind.Free, CommunityLicenseReason.Individual ).IsSuccess );

        Assert.Equal( "Metalama Community", this.LicenseRegistrationService.RegisteredLicenses.Single().Description );
        Assert.Equal( CommunityLicenseReason.Individual, this.ConfigurationManager!.Get<LicensingConfiguration>().CommunityLicenseReason );
    }

    /// <summary>
    /// The edition that Metalama 2025.0 and earlier issued is still registrable, because a user going back to one of
    /// those versions has no other way to license it. It never expires: a version that reads it cannot renew it.
    /// </summary>
    [Fact]
    public void TheLegacyFreeEditionNeverExpires()
    {
        var edition = this.GetEdition( SelfRegisteredEditionKind.LegacyFree );

        Assert.Equal( SelfRegisteredEditionKind.LegacyFree, edition.Kind );

        var license = edition.CreateLicense( this.CreateEditionContext() );

        Assert.Equal( LicenseProduct.MetalamaFree, license.Product );
        Assert.Null( license.ValidTo );
    }

    /// <summary>
    /// It is registrable, which nothing asserted before: the edition had no test of its own.
    /// </summary>
    [Fact]
    public void TheLegacyFreeEditionRegisters()
    {
        Assert.True( this.RegisterEdition( SelfRegisteredEditionKind.LegacyFree ).IsSuccess );

        Assert.Equal( "Metalama Free (legacy)", this.LicenseRegistrationService.RegisteredLicenses.Single().Description );
    }

    /// <summary>
    /// Only the trial and the legacy free edition are offered from the command line, and neither of them during
    /// setup: Metalama runs unlicensed, so the page offers the open source edition instead.
    /// </summary>
    [Fact]
    public void OnlyTheTrialIsOfferedDuringSetup()
    {
        Assert.Equal(
            new[] { SelfRegisteredEditionKind.LegacyFree, SelfRegisteredEditionKind.Trial },
            this.Catalog.SelfRegisteredEditions.Where( e => e.IsAvailableFromCommandLine ).Select( e => e.Kind ).ToArray() );

        Assert.Equal(
            new[] { SelfRegisteredEditionKind.Trial },
            this.Catalog.SelfRegisteredEditions.Where( e => e.SetupTitle != null ).Select( e => e.Kind ).ToArray() );
    }

    /// <summary>
    /// An edition of another family cannot be registered, so a caller holding the wrong catalog is refused rather
    /// than writing a key this product cannot consume.
    /// </summary>
    [Fact]
    public void AnEditionOfAnotherFamilyIsRefused()
    {
        var foreignEdition = PostSharpProduct.Instance.LicenseProductCatalog.SelfRegisteredEditions
            .Single( e => e.Alias == "essentials" );

        var result = this.LicenseRegistrationService.Register( foreignEdition );

        Assert.False( result.IsSuccess );
        Assert.Empty( this.LicenseRegistrationService.RegisteredLicenses );
    }
}
#pragma warning restore CS0618
