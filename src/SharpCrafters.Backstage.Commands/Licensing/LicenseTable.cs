// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Licensing;
using SharpCrafters.Backstage.Licensing.Registration;
using Spectre.Console;
using System;
using System.Globalization;

namespace SharpCrafters.Backstage.Commands.Licensing;

/// <summary>
/// Renders a registered licence, whether it is a licence key or a license server, as a table of fields.
/// </summary>
/// <remarks>
/// It is shared by the command that lists the registered licences and the one that tests a license server, so that a
/// user who runs the two sees the same description of the same licence.
/// </remarks>
internal static class LicenseTable
{
    public static Table Create( LicenseRegistrationProperties license )
    {
        var table = new Table();
        table.AddColumn( "Field" );
        table.AddColumn( "Value" );

        void AddRow( string description, string? value )
        {
            if ( value != null )
            {
                table.AddRow( description, value );
            }
        }

        // A license server is named by its URL. The licence key it leases is temporary and is deliberately not shown
        // as a registered license key, because the user did not register it and cannot register it.
        AddRow( "License Server", license.LicenseServerUrl );

        AddRow( "License ID", license.LicenseId?.ToString( CultureInfo.InvariantCulture ) );

        if ( license.LicenseId != null && license.LicenseServerUrl == null )
        {
            AddRow( "License Key", license.LicenseString );
        }

        AddRow( "Description", license.Description );
        AddRow( "Licensee", license.Licensee );

        string? expiration = null;

        if ( license.Perpetual != null )
        {
            expiration = license.Perpetual.Value ? "Never (perpetual license)" : Format( license.ValidTo );
        }

        AddRow( "License Expiration", expiration );
        AddRow( "Maintenance Expiration", Format( license.SubscriptionEndDate ) );

        if ( license.Lease is { } lease )
        {
            AddRow( "Lease Expiration", Format( lease.EndTime ) );
            AddRow( "Lease Renewal", Format( lease.RenewTime ) );
        }

        AddRow( "Eligible Servicing Phases", license.ServicingPhase.GetDisplayName( true ) );
        AddRow( "License Audit", license.Auditable ? "Yes" : "No" );

        return table;
    }

    private static string? Format( DateTime? dateTime ) => dateTime?.ToString( "D", CultureInfo.InvariantCulture );
}
