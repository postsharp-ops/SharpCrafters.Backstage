// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Application;
using SharpCrafters.Backstage.Extensibility;
using SharpCrafters.Backstage.Licensing.Registration;
using System;
using System.Linq;

namespace SharpCrafters.Backstage.Commands.Licensing
{
    internal class ListLicensesCommand : BaseCommand<BaseCommandSettings>
    {
        protected override void Execute( ExtendedCommandContext context, BaseCommandSettings settings )
        {
            var licenseRegistrationService = context.ServiceProvider.GetRequiredBackstageService<ILicenseRegistrationService>();
            var productName = context.ServiceProvider.GetRequiredBackstageService<ProductProfile>().Name;

            // Listing contacts no license server: it reports the lease that is already held, so that showing what is
            // registered never waits for a network.
            var licenses = licenseRegistrationService.RegisteredLicenses.ToList();
            var unsupportedVersions = licenseRegistrationService.UnsupportedRegisteredLicenseVersions.ToList();

            if ( licenses.Count > 0 )
            {
                foreach ( var license in licenses )
                {
                    context.Console.WriteMessage(
                        (license.LicenseServerUrl == null
                            ? "The following license is currently registered:"
                            : "The following license server is currently registered:")
                        + Environment.NewLine );

                    context.Console.Out.Write( LicenseTable.Create( license ) );

                    if ( license is { LicenseServerUrl: not null, Lease: null } )
                    {
                        context.Console.WriteMessage( "No license has been leased from this license server yet." );
                    }
                }
            }
            else if ( unsupportedVersions.Count == 0 )
            {
                context.Console.WriteWarning( $"No {productName} license is currently registered." );
            }

            // A license key of an unsupported group is not deserialized, so the minimal version of the group is the
            // only information that the current version has about it.
            foreach ( var unsupportedVersion in unsupportedVersions )
            {
                context.Console.WriteWarning(
                    $"A registered license key requires {productName} {unsupportedVersion} or later. "
                    + $"Upgrade {productName} to this version to use that license key." );
            }
        }
    }
}
