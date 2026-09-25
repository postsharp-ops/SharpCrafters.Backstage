// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using System;

namespace SharpCrafters.Backstage.Commands.Maintenance;

/// <summary>
/// The <c>kill</c> command, which is <c>shutdown --force</c> with a shorter timeout: it cancels the work of the processes
/// and ends the ones that do not exit.
/// </summary>
internal class KillCommand : BaseCommand<KillCommandSettings>
{
    protected override void Execute( ExtendedCommandContext context, KillCommandSettings settings )
    {
        context.Console.WriteHeading( $"Killing the {context.BackstageCommandOptions.ProductProfile.Name} processes" );

        var running = ProcessShutdownRunner.Run( context, true, TimeSpan.FromSeconds( settings.Timeout ), !settings.NoWarn );

        if ( running > 0 )
        {
            throw new CommandException( $"{running} process{(running == 1 ? " is" : "es are")} still running." );
        }
    }
}
