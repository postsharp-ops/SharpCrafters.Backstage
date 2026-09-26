// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using Metalama.Backstage.Tools;
using SharpCrafters.Backstage.Commands;
using SharpCrafters.Backstage.Maintenance;
using Spectre.Console.Cli;

namespace Metalama.Backstage.TestTool;

internal static class Program
{
    public static int Main( string[] args )
    {
        var app = new CommandApp();

        // The integrated development environments load the analyzers of Metalama, so they are reported by the shutdown
        // commands, as Metalama.Tool does.
        var options = new BackstageCommandOptions(
            new ApplicationInfo(),
            MetalamaProduct.Instance,
            builder => builder.AddTools(),
            registerServices: builder => builder.AddDevelopmentEnvironmentShutdownStrategy() );

        BackstageCommandFactory.ConfigureCommandApp(
            app,
            options,
            builder => builder.AddCommand<ThrowCommand>( "throw" ).WithData( options ).WithDescription( "Throws an exception to test exception handling." ) );

        return app.Run( args );
    }
}