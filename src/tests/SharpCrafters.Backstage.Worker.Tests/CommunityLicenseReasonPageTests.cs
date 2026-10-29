// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using Metalama.Backstage;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SharpCrafters.Backstage.Licensing;
using SharpCrafters.Backstage.Testing;
using SharpCrafters.Backstage.Worker.Pages;
using SharpCrafters.Backstage.Worker.Pages.Shared;
using Xunit;

namespace SharpCrafters.Backstage.Worker.Tests;

/// <summary>
/// Tests the page that asks why the user is entitled to Metalama Community.
/// </summary>
/// <remarks>
/// The page reads and writes <see cref="GlobalState"/>, which is static. The tests of this class run one after the
/// other, and no other test class uses <see cref="GlobalState"/>.
/// </remarks>
public sealed class CommunityLicenseReasonPageTests : TestsBase
{
    public CommunityLicenseReasonPageTests( ITestOutputHelper logger ) : base( logger )
    {
        GlobalState.SelectedAction = SelectedAction.SelfRegisteredEdition;
        GlobalState.SelfRegisteredEditionAlias = "community";
        GlobalState.CommunityLicenseReason = CommunityLicenseReason.None;
    }

    private static CommunityLicenseReasonPageModel CreateModel() => new( MetalamaProduct.Instance.LicenseProductCatalog );

    private static void AssertRedirect( string url, IActionResult result ) => Assert.Equal( url, Assert.IsType<RedirectResult>( result ).Url );

    [Theory]
    [InlineData( CommunityLicenseReason.Individual )]
    [InlineData( CommunityLicenseReason.SmallBusiness )]
    [InlineData( CommunityLicenseReason.OpenSource )]
    [InlineData( CommunityLicenseReason.NonCommercial )]
    public void AValidReasonIsSavedBeforeTheConsents( CommunityLicenseReason reason )
    {
        var model = CreateModel();
        model.Reason = reason;

        AssertRedirect( "/Consents", model.OnPost() );
        Assert.Equal( reason, GlobalState.CommunityLicenseReason );
        Assert.Empty( model.ErrorMessages );
    }

    [Theory]
    [InlineData( CommunityLicenseReason.None )]
    [InlineData( (CommunityLicenseReason) 99 )]
    public void AMissingOrInvalidReasonIsRefused( CommunityLicenseReason reason )
    {
        var model = CreateModel();
        model.Reason = reason;

        Assert.IsType<PageResult>( model.OnPost() );
        Assert.Equal( CommunityLicenseReason.None, GlobalState.CommunityLicenseReason );
        Assert.Equal( "Please choose how you will use Metalama Community.", Assert.Single( model.ErrorMessages ) );
    }

    [Theory]
    [InlineData( SelectedAction.Register, "community" )]
    [InlineData( SelectedAction.SelfRegisteredEdition, "try" )]
    [InlineData( SelectedAction.SelfRegisteredEdition, null )]
    public void ARequestWithoutAnEditionThatRequiresAReasonGoesBackToTheChoice( SelectedAction action, string? alias )
    {
        GlobalState.SelectedAction = action;
        GlobalState.SelfRegisteredEditionAlias = alias;

        var model = CreateModel();
        model.Reason = CommunityLicenseReason.Individual;

        AssertRedirect( "/ChooseLicenseKind", model.OnPost() );
        AssertRedirect( "/ChooseLicenseKind", CreateModel().OnGet() );
        Assert.Equal( CommunityLicenseReason.None, GlobalState.CommunityLicenseReason );
    }

    [Fact]
    public void TheSavedReasonIsShownAgain()
    {
        // The Back button of the consents returns to this page, which shows the reason chosen before.
        GlobalState.CommunityLicenseReason = CommunityLicenseReason.OpenSource;

        var model = CreateModel();

        Assert.IsType<PageResult>( model.OnGet() );
        Assert.Equal( CommunityLicenseReason.OpenSource, model.Reason );
    }
}
