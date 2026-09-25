// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using System;

namespace SharpCrafters.Backstage.Commands.Maintenance;

/// <summary>
/// The <c>shutdown</c> command, also named <c>kill</c>: stops the processes that keep the files of the product locked
/// after a build.
/// </summary>
/// <remarks>
/// What is stopped, and how, is decided by the registered <see cref="SharpCrafters.Backstage.Maintenance.IProcessShutdownStrategy"/>
/// implementations. The exit code is one when a process may still be running, so that a build script can tell whether the
/// files of the product are free.
/// </remarks>
internal class ShutdownCommand : BaseCommand<ShutdownCommandSettings>
{
    protected override void Execute( ExtendedCommandContext context, ShutdownCommandSettings settings )
    {
        context.Console.WriteHeading( $"Shutting down the {context.BackstageCommandOptions.ProductProfile.Name} processes" );

        var running = ProcessShutdownRunner.Run( context, settings.Force, TimeSpan.FromSeconds( settings.Timeout ), !settings.NoWarn );

        if ( running > 0 )
        {
            throw new CommandException(
                $"{running} process{(running == 1 ? " is" : "es are")} still running{(settings.Force ? "" : "; use --force to end the ones that can be ended")}." );
        }
    }
}
