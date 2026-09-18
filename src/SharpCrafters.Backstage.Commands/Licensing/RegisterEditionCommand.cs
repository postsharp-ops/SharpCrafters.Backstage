// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Extensibility;
using SharpCrafters.Backstage.Licensing;
using SharpCrafters.Backstage.Licensing.Registration;
using System;
using System.Linq;

namespace SharpCrafters.Backstage.Commands.Licensing;

/// <summary>
/// Registers an edition that the user can obtain by asking for it: a free edition, or one that an earlier version of
/// the product issued.
/// </summary>
/// <remarks>
/// One command serves every such edition. It finds the one it was invoked for by the name it was invoked under,
/// which is the alias the product family gave it, so that adding an edition to a family adds a command without
/// adding a class.
/// </remarks>
internal class RegisterEditionCommand : BaseCommand<RegisterEditionCommandSettings>
{
    protected override void Execute( ExtendedCommandContext context, RegisterEditionCommandSettings settings )
    {
        var edition = context.BackstageCommandOptions.Product.LicenseProductCatalog.SelfRegisteredEditions
                          .FirstOrDefault( e => string.Equals( e.Alias, context.CommandName, StringComparison.OrdinalIgnoreCase ) )
                      ?? throw new InvalidOperationException( $"There is no self-registered edition named '{context.CommandName}'." );

        if ( edition.RequiresReason && settings.Reason == CommunityLicenseReason.None )
        {
            throw new CommandException( "You must provide a value for the --reason option." );
        }

        var service = context.ServiceProvider.GetRequiredBackstageService<ILicenseRegistrationService>();

        var result = edition.RegisterAction( service, settings.Reason );

        if ( !result.IsSuccess )
        {
            throw new CommandException( result.ErrorMessage );
        }

        context.Console.WriteSuccess( edition.SuccessMessage );
    }
}
