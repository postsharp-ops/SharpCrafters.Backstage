// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Extensibility;
using SharpCrafters.Backstage.Licensing.Registration;
using System.Threading.Tasks;

namespace SharpCrafters.Backstage.Commands.Licensing;

/// <summary>
/// Contacts a license server and prints the licence it would lease, without registering it.
/// </summary>
/// <remarks>
/// Diagnosing an on-premises license server is the most common support interaction for this feature, and registering
/// is the wrong tool for it, because it changes the licence the product uses.
/// </remarks>
internal class TestLicenseServerCommand : BaseAsyncCommand<TestLicenseServerCommandSettings>
{
    protected override async Task ExecuteAsync( ExtendedCommandContext context, TestLicenseServerCommandSettings settings )
    {
        var service = context.ServiceProvider.GetRequiredBackstageService<ILicenseRegistrationService>();

        var result = await service.TestLicenseServerAsync( settings.Url );

        if ( !result.IsSuccess )
        {
            throw new CommandException( result.ErrorMessage );
        }

        context.Console.WriteSuccess( $"The license server '{settings.Url}' is reachable and leases the following license:" );
        context.Console.Out.Write( LicenseTable.Create( result.RegisteredLicense ) );
        context.Console.WriteMessage( "The license server has not been registered. Use 'license register' to register it." );
    }
}
