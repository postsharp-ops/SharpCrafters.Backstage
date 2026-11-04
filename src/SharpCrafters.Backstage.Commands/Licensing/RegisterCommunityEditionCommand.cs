// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Licensing;
using SharpCrafters.Backstage.Licensing.Registration;
using Spectre.Console;
using System.Globalization;

namespace SharpCrafters.Backstage.Commands.Licensing;

/// <summary>
/// Registers an edition that requires the user to say why they are entitled to it, which is Metalama Community.
/// </summary>
/// <remarks>
/// The reason comes from the <c>--reason</c> option. When the option is omitted, the command asks for the reason on
/// the standard input, and it fails when no input is available.
/// </remarks>
internal sealed class RegisterCommunityEditionCommand : RegisterEditionCommand<RegisterCommunityEditionCommandSettings>
{
    protected override CommunityLicenseReason GetReason(
        ExtendedCommandContext context,
        RegisterCommunityEditionCommandSettings settings,
        SelfRegisteredEdition edition )
    {
        if ( settings.Reason is { } reason )
        {
            if ( !CommunityLicenseReasonExtensions.SelectableReasons.Contains( reason ) )
            {
                throw new CommandException(
                    $"'{reason}' is not a valid reason. Use one of: {string.Join( ", ", CommunityLicenseReasonExtensions.SelectableReasons )}." );
            }

            return reason;
        }

        return AskReason( context, edition );
    }

    private static CommunityLicenseReason AskReason( ExtendedCommandContext context, SelfRegisteredEdition edition )
    {
        var reasons = CommunityLicenseReasonExtensions.SelectableReasons;
        var input = context.BackstageCommandOptions.StandardInput;

        context.Console.WriteImportantMessage( CommunityLicenseReasonExtensions.Question );
        context.Console.Out.WriteLine( CommunityLicenseReasonExtensions.Introduction );

        for ( var i = 0; i < reasons.Length; i++ )
        {
            context.Console.Out.WriteLine( $"  {i + 1}. {reasons[i].GetDescription()}" );
        }

        while ( true )
        {
            context.Console.Out.Write( $"Enter a number from 1 to {reasons.Length}: " );

            var line = input.ReadLine();

            if ( line == null )
            {
                context.Console.Out.WriteLine();

                throw new CommandException(
                    $"{edition.DisplayName} requires you to say why you are entitled to it, and no input is available to ask. "
                    + "Run the command from an interactive terminal, or use the --reason option." );
            }

            if ( int.TryParse( line.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var choice ) && choice >= 1 && choice <= reasons.Length )
            {
                return reasons[choice - 1];
            }

            context.Console.WriteWarning( $"'{line.Trim()}' is not a number from 1 to {reasons.Length}." );
        }
    }
}
