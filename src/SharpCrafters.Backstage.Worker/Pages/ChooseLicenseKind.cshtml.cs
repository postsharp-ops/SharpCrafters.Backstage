// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SharpCrafters.Backstage.Licensing;
using SharpCrafters.Backstage.UserInterface;
using SharpCrafters.Backstage.Worker.Pages.Shared;

namespace SharpCrafters.Backstage.Worker.Pages;

#pragma warning disable SA1649

public class ChooseLicenseKindPageModel : PageModel
{
    private readonly ILicenseProductCatalog _catalog;

    public ChooseLicenseKindPageModel( IWebLinks webLinks, ILicenseProductCatalog catalog )
    {
        this.WebLinks = webLinks;
        this._catalog = catalog;
    }

    public IWebLinks WebLinks { get; }

    public IActionResult OnPost( string action )
    {
        switch ( action )
        {
            // Checked here and not only where the choice is offered, because the page is reached over a local
            // server and a request is not obliged to come from the form. A product that does nothing without a
            // license must not be left believing that it has been set up.
            case "UseOpenSource" when this._catalog.HasUnlicensedEdition:
                GlobalState.SelectedAction = SelectedAction.OpenSource;

                return this.Redirect( "/DoneOpenSource" );

            case "StartTrial":
                GlobalState.SelectedAction = SelectedAction.Trial;

                return this.Redirect( "/Consents" );

            case "Skip":
                GlobalState.SelectedAction = SelectedAction.Skip;

                return this.Redirect( "/Consents" );

            case "RegisterKey":
                GlobalState.SelectedAction = SelectedAction.Register;

                return this.Redirect( "/LicenseKey" );
        }

        return this.Page();
    }
}