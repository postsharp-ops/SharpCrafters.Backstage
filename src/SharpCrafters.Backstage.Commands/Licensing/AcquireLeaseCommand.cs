// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Extensibility;
using SharpCrafters.Backstage.Licensing.LicenseServer;
using SharpCrafters.Backstage.Licensing.Registration;
using System.Threading.Tasks;

namespace SharpCrafters.Backstage.Commands.Licensing;

/// <summary>
/// Acquires a lease from the registered license server and prints the licence it leases.
/// </summary>
/// <remarks>
/// <para>
/// This does what a build does, which is the point: the server is contacted, a seat is taken and the lease is stored,
/// so what the user sees is what their next build will see rather than the result of a separate code path. The name
/// says so. Calling it a test would be a lie, because there is no way to ask a license server what it would lease
/// without leasing it.
/// </para>
/// <para>
/// Without <c>--force</c> it is also what a build costs: a lease that is still valid and not yet due for renewal is
/// used as it stands, and nothing is sent.
/// </para>
/// </remarks>
internal class AcquireLeaseCommand : BaseAsyncCommand<AcquireLeaseCommandSettings>
{
    protected override async Task ExecuteAsync( ExtendedCommandContext context, AcquireLeaseCommandSettings settings )
    {
        var service = context.ServiceProvider.GetRequiredBackstageService<ILicenseRegistrationService>();

        var result = await service.AcquireLeaseAsync( settings.Force );

        if ( !result.IsSuccess )
        {
            throw new CommandException( result.ErrorMessage );
        }

        var licenseServerUrl = result.RegisteredLicense.LicenseServerUrl;

        context.Console.WriteSuccess( $"The license server '{licenseServerUrl}' leases the following license:" );
        context.Console.Out.Write( LicenseTable.Create( result.RegisteredLicense ) );

        if ( licenseServerUrl != null
             && context.ServiceProvider.GetRequiredBackstageService<LicenseServerUrlValidator>()
                 .TryValidate( licenseServerUrl, out _, out var warning )
             && warning != null )
        {
            context.Console.WriteWarning( warning );
        }
    }
}
