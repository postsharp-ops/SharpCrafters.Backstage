// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Application;
using SharpCrafters.Backstage.Extensibility;
using SharpCrafters.Backstage.Maintenance;

namespace SharpCrafters.Backstage.Commands.Maintenance;

internal class KillCommand : BaseCommand<KillCommandSettings>
{
    protected override void Execute( ExtendedCommandContext context, KillCommandSettings settings )
    {
        var productName = context.ServiceProvider.GetRequiredBackstageService<ProductProfile>().Name;

        context.Console.WriteHeading( $"Killing {productName} processes" );

        var processManager = context.ServiceProvider.GetRequiredBackstageService<IProcessManager>();

        processManager.KillCompilerProcesses( !settings.NoWarn );

        context.Console.WriteSuccess( $"{productName} processes have been killed." );
    }
}