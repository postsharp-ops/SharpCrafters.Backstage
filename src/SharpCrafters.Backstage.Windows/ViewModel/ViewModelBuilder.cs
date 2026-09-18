// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Application;
using SharpCrafters.Backstage.Extensibility;
using SharpCrafters.Backstage.Licensing;
using SharpCrafters.Backstage.Licensing.Registration;
using SharpCrafters.Backstage.UserInterface;
using SharpCrafters.Backstage.UserInterface.Toasts;
using SharpCrafters.Backstage.Utilities;
using SharpCrafters.Backstage.Windows.Commands;
using System;
using System.Linq;
using System.Diagnostics.CodeAnalysis;

namespace SharpCrafters.Backstage.Windows.ViewModel;

internal static class ViewModelBuilder
{
    public static bool TryGetNotificationViewModel(
        IServiceProvider serviceProvider,
        NotifyCommandSettings settings,
        [NotNullWhen( true )] out NotificationViewModel? viewModel )
    {
        var activationArguments = new ActivationArguments( settings );
        var webLinks = serviceProvider.GetRequiredBackstageService<IWebLinks>();
        var productName = serviceProvider.GetRequiredBackstageService<ProductProfile>().Name;
        var catalog = serviceProvider.GetRequiredBackstageService<ILicenseProductCatalog>();

        if ( settings.Kind == ToastNotificationKinds.RequiresLicense.Name )
        {
            viewModel = new NotificationViewModel(
                settings.Kind,
                catalog.PremiumEditionDisplayName,
                $"This project uses a premium {productName} feature. Try {catalog.PremiumEditionDisplayName} for 45 days or register a license key.",
                new CommandActionViewModel( "Options", activationArguments.Setup ) );

            return true;
        }
        else if ( settings.Kind == ToastNotificationKinds.VsxNotInstalled.Name )
        {
            // TODO: The name of the extension and the list of its features are specific to Metalama and PostSharp, and are not
            // product neutral. See #2018.
            viewModel = new NotificationViewModel(
                settings.Kind,
                $"Install Visual Studio Tools for {productName}",
                $"to enhance your {productName} coding experience: syntax highlighting, CodeLens, and diff preview.",
                new UriActionViewModel( "Install", webLinks.InstallVsx ) );

            return true;
        }
        else if ( settings.Kind == ToastNotificationKinds.LicenseExpiring.Name )
        {
            viewModel = new NotificationViewModel(
                settings.Kind,
                settings.Title ?? $"Your {productName} license is expiring",
                settings.Text ?? $"Renew your {productName} subscription",
                new UriActionViewModel( "Renew", webLinks.RenewSubscription ) );

            return true;
        }
        else if ( settings.Kind == ToastNotificationKinds.TrialExpiring.Name )
        {
            viewModel = new NotificationViewModel(
                settings.Kind,
                settings.Title ?? $"Your {productName} trial is expiring",
                settings.Text ?? GetTrialExpiringText( productName, catalog ),
                new CommandActionViewModel( "Open", activationArguments.Setup ) );

            return true;
        }
        else if ( settings.Kind == ToastNotificationKinds.SubscriptionExpiring.Name )
        {
            viewModel = new NotificationViewModel(
                settings.Kind,
                settings.Title ?? $"Your {productName} subscription is expiring",
                settings.Text ?? "Renew your subscription to benefit from continued updates and support.",
                new CommandActionViewModel( "Open", activationArguments.Setup ) );

            return true;
        }
        else if ( settings.Kind == ToastNotificationKinds.LicenseServerUnreachable.Name )
        {
            // The action opens the setup page, which is where the user sees what licenses this machine and can
            // register a license key if the server stays down. Nothing here can reach the server for them.
            viewModel = new NotificationViewModel(
                settings.Kind,
                settings.Title ?? "License server unreachable",
                settings.Text ?? $"{productName} could not renew the license of this machine.",
                new CommandActionViewModel( "Options", activationArguments.Setup ) );

            return true;
        }
        else if ( settings.Kind == ToastNotificationKinds.ExceptionReport.Name )
        {
            // Open the worker review page (formatted report + Report button + per-category auto-report checkbox)
            // instead of opening the raw report file. See #1674.
            //
            // This is the only notification kind without Snooze and Mute. Both act on the whole kind, so on this
            // notification they silenced every error report on the machine: Mute permanently and irreversibly from the
            // product, Snooze for an hour. Since the notification is the only way to approve a report, that turned a
            // single click into the end of error reporting. The review page offers the per-issue equivalent ("never
            // report this error"), and the privacy page remains the visible, reversible way to turn the whole channel
            // off. Every other notification kind keeps both buttons. See #1751.
            // Review comes first and stays the primary action, so the default gesture is still to look before sending.
            // Report is the one-click path for the user who is willing to report without inspecting the payload: it
            // sends the very same scrubbed report. "Never report this error" deliberately stays on the review page, so
            // that opting an issue out costs a page visit while opting in does not. See #1751.
            viewModel = new NotificationViewModel(
                settings.Kind,
                settings.Title ?? $"{productName} failed",
                settings.Text ?? $"{productName} encountered an unhandled exception.",
                new CommandActionViewModel( "Review", activationArguments.OpenExceptionReport ),
                new CommandActionViewModel( "Report", activationArguments.ReportException ) ) { CanMute = false, CanSnooze = false };

            return true;
        }
        else if ( settings.Kind == ToastNotificationKinds.TelemetryNotice.Name )
        {
            viewModel = new NotificationViewModel(
                settings.Kind,
                $"Welcome to {productName}",
                $"To improve the product, {productName} collects anonymous usage data. Click to learn more or opt out.",
                new CommandActionViewModel( "Privacy options", activationArguments.OpenPrivacyOptions ),
                new UriActionViewModel( "Learn more", webLinks.DisableTelemetryInstructions ) ) { CanMute = false, CanSnooze = false };

            return true;
        }
        else if ( settings.Kind == ToastNotificationKinds.News.Name )
        {
            // Defense in depth: ensure the URI uses a safe (http/https) scheme before it reaches Windows protocol
            // activation. The primary validation is in RssClient, but the URI is server-controlled, so we re-check here.
            // See issue #1647.
            if ( !UrlHelper.IsSafe( settings.Uri, out var newsUri ) )
            {
                viewModel = null;

                return false;
            }

            viewModel = new NotificationViewModel(
                settings.Kind,
                $"{productName} Blog Update",
                settings.Title,
                new UriActionViewModel( "Read", newsUri ),
                new CommandActionViewModel( "Options", activationArguments.OpenRssOptions ) ) { CanMute = false, CanSnooze = false };

            return true;
        }
        else if ( ToastNotificationKinds.All.ContainsKey( settings.Kind ) && settings.Title != null )
        {
            // A kind this build does not know by name, but which the product does. It is shown with the words the
            // product supplied and no tailored action.
            //
            // The branch exists because the alternative is silence: every kind above needs a case here, nothing
            // fails to build when one is added without it, and the notification is then raised, recorded and never
            // seen. A notification that cannot be seen is worse than one with a generic button, and the desktop
            // application is versioned separately from the product that publishes the kinds, so the two are not
            // always in step even when nobody forgets.
            viewModel = new NotificationViewModel( settings.Kind, settings.Title, settings.Text );

            return true;
        }
        else
        {
            viewModel = null;

            return false;
        }
    }

    /// <summary>
    /// Says what a user whose trial is expiring can do, which depends on whether the product family has a free
    /// edition to fall back to.
    /// </summary>
    private static string GetTrialExpiringText( string productName, ILicenseProductCatalog catalog )
    {
        // The edition names itself. Reading the product out of the key it grants named the premium product for a
        // family whose free edition is expressed through the license type, and so invited a PostSharp user whose
        // trial was ending to activate the edition they have to buy.
        var freeEdition = catalog.SelfRegisteredEditions.FirstOrDefault( e => e.Kind == SelfRegisteredEditionKind.Free );

        return freeEdition == null
            ? $"Register a license key to keep using {productName}."
            : $"Register a license key or activate {freeEdition.DisplayName}.";
    }
}