// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Extensibility;
using SharpCrafters.Backstage.Licensing.LicenseServer;
using SharpCrafters.Backstage.Licensing.Registration;
using System.Threading.Tasks;

namespace SharpCrafters.Backstage.Commands.Licensing;

/// <summary>
/// Registers a license string, which is either a license key or the URL of a license server.
/// </summary>
/// <remarks>
/// The command is asynchronous because registering the URL of a license server contacts it, so that the user learns
/// at once whether the server answers and has a licence for them.
/// </remarks>
internal class RegisterLicenseCommand : BaseAsyncCommand<RegisterLicenseCommandSettings>
{
    // We no longer collect license activations reports.
    // We only collect license usage reports.

    protected override async Task ExecuteAsync( ExtendedCommandContext context, RegisterLicenseCommandSettings settings )
    {
        var service = context.ServiceProvider.GetRequiredBackstageService<ILicenseRegistrationService>();

        var result = await service.RegisterLicenseAsync( settings.License );

        if ( !result.IsSuccess )
        {
            throw new CommandException( result.ErrorMessage );
        }

        context.Console.WriteSuccess(
            result.RegisteredLicense.LicenseServerUrl == null
                ? $"The license key '{settings.License}' has been registered."
                : $"The license server '{result.RegisteredLicense.LicenseServerUrl}' has been registered." );

        // Registering is the moment the user can still choose a different URL, so the warning belongs here as much as
        // it belongs to the builds that will lease from this server.
        if ( result.RegisteredLicense.LicenseServerUrl != null
             && context.ServiceProvider.GetRequiredBackstageService<LicenseServerUrlValidator>()
                 .TryValidate( result.RegisteredLicense.LicenseServerUrl, out _, out var warning )
             && warning != null )
        {
            context.Console.WriteWarning( warning );
        }
    }
}
