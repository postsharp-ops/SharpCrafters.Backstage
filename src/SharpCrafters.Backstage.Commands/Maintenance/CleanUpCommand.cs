// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Maintenance;
using System;

namespace SharpCrafters.Backstage.Commands.Maintenance;

internal class CleanUpCommand : BaseCommand<CleanUpCommandSettings>
{
    protected override void Execute( ExtendedCommandContext context, CleanUpCommandSettings settings )
    {
        if ( settings is { All: true, DoNotKill: false } )
        {
            context.Console.WriteHeading( $"Killing the {context.BackstageCommandOptions.ProductProfile.Name} processes" );

            // The processes are ended before the clean-up, unless --no-kill is used, because they hold the files that it
            // deletes. The clean-up proceeds even if some processes remain, as it always has.
            ProcessShutdownRunner.Run( context, true, TimeSpan.FromSeconds( 10 ), !settings.NoWarn );
        }

        context.Console.WriteHeading( "Cleaning up temporary files. " );

        var tempFileManager = new TempFileManager( context.ServiceProvider );

        tempFileManager.CleanTempDirectories( true, settings.All );

        if ( settings.All )
        {
            context.Console.WriteSuccess( "Temporary files have been cleaned up." );
        }
        else
        {
            context.Console.WriteSuccess( "Unused temporary files have been cleaned up." );
        }
    }
}