// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using PostSharp.Backstage.Tools;
using SharpCrafters.Backstage.Commands;
using Spectre.Console.Cli;

namespace PostSharp.Backstage.TestTool;

internal static class Program
{
    public static int Main( string[] args )
    {
        var app = new CommandApp();

        var options = new BackstageCommandOptions( new ApplicationInfo(), PostSharpProduct.Instance, builder => builder.AddTools() );

        BackstageCommandFactory.ConfigureCommandApp( app, options );

        return app.Run( args );
    }
}
