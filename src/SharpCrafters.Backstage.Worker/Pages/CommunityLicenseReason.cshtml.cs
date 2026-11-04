// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SharpCrafters.Backstage.Licensing;
using SharpCrafters.Backstage.Licensing.Registration;
using SharpCrafters.Backstage.Worker.Pages.Shared;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SharpCrafters.Backstage.Worker.Pages;

#pragma warning disable SA1649

/// <summary>
/// Asks why the user is entitled to the edition they chose, for an edition that requires a reason. The page comes
/// between the choice of the edition and the consents.
/// </summary>
internal class CommunityLicenseReasonPageModel : PageModel
{
    private readonly ILicenseProductCatalog _catalog;

    public CommunityLicenseReasonPageModel( ILicenseProductCatalog catalog )
    {
        this._catalog = catalog;
    }

    [BindProperty]
    public CommunityLicenseReason Reason { get; set; }

    public List<string> ErrorMessages { get; } = [];

    public IActionResult OnGet()
    {
        var edition = this.GetEdition();

        if ( edition == null )
        {
            return this.Redirect( "/ChooseLicenseKind" );
        }

        this.Reason = GlobalState.CommunityLicenseReason;

        return this.Page();
    }

    public IActionResult OnPost()
    {
        // Checked again here, because the page is reached over a local server and a request is not obliged to come
        // from the form.
        var edition = this.GetEdition();

        if ( edition == null )
        {
            return this.Redirect( "/ChooseLicenseKind" );
        }

        if ( !CommunityLicenseReasonExtensions.SelectableReasons.Contains( this.Reason ) )
        {
            this.ErrorMessages.Add( $"Please choose how you will use {edition.DisplayName}." );

            return this.Page();
        }

        GlobalState.CommunityLicenseReason = this.Reason;

        return this.Redirect( "/Consents" );
    }

    /// <summary>
    /// Gets the edition that the user chose, or <see langword="null"/> when the choice is not an edition that requires
    /// a reason.
    /// </summary>
    private SelfRegisteredEdition? GetEdition()
        => GlobalState.SelectedAction == SelectedAction.SelfRegisteredEdition
            ? this._catalog.SelfRegisteredEditions.FirstOrDefault(
                e => e.RequiresReason && string.Equals( e.Alias, GlobalState.SelfRegisteredEditionAlias, StringComparison.OrdinalIgnoreCase ) )
            : null;
}
