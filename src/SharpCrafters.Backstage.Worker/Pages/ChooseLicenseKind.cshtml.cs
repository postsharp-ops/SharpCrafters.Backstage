// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SharpCrafters.Backstage.Licensing;
using SharpCrafters.Backstage.UserInterface;
using SharpCrafters.Backstage.Worker.Pages.Shared;
using System;
using System.Linq;

namespace SharpCrafters.Backstage.Worker.Pages;

#pragma warning disable SA1649

public class ChooseLicenseKindPageModel : PageModel
{
    /// <summary>
    /// The prefix of the action that names an edition, which distinguishes it from the fixed choices.
    /// </summary>
    private const string _editionActionPrefix = "Register:";

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

        // One of the editions that the product family offers, named by the alias that the choice carries. The alias
        // is looked up rather than trusted, for the reason given above: a request need not come from the form.
        if ( action?.StartsWith( _editionActionPrefix, StringComparison.Ordinal ) == true )
        {
            var alias = action.Substring( _editionActionPrefix.Length );

            var edition = this._catalog.SelfRegisteredEditions
                .FirstOrDefault( e => e.SetupTitle != null && string.Equals( e.Alias, alias, StringComparison.OrdinalIgnoreCase ) );

            if ( edition != null )
            {
                GlobalState.SelectedAction = SelectedAction.SelfRegisteredEdition;
                GlobalState.SelfRegisteredEditionAlias = edition.Alias;

                return this.Redirect( "/Consents" );
            }
        }

        return this.Page();
    }
}