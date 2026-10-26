// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using Metalama.Backstage;
using SharpCrafters.Backstage.Testing;
using Spectre.Console;
using Spectre.Console.Cli;
using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace SharpCrafters.Backstage.Commands.Tests.Commands;

/// <summary>
/// <c>--version</c>, which writes the version of the package of the application.
/// </summary>
public sealed class VersionCommandTests : CommandsTestsBase
{
    public VersionCommandTests( ITestOutputHelper logger ) : base( logger, new TestApplicationInfo( "Tool", false, "2027.0.6-preview", DateTime.Now ) ) { }

    [Fact]
    public async Task TheVersionIsThatOfThePackageOfTheApplication()
    {
        var console = new StringWriter();
        var app = new CommandApp();

        BackstageCommandFactory.ConfigureCommandApp(
            app,
            new BackstageCommandOptions( this, MetalamaProduct.Instance, console, console, AnsiSupport.No, this.ApplicationInfo ) );

        // Spectre.Console writes the version to its own console, which is the process console unless it is configured.
        app.Configure( c => c.ConfigureConsole( AnsiConsole.Create( new AnsiConsoleSettings { Out = new AnsiConsoleOutput( console ), Ansi = AnsiSupport.No } ) ) );

        Assert.Equal( 0, await app.RunAsync( ["--version"] ) );
        Assert.Equal( "2027.0.6-preview", console.ToString().Trim() );
    }

    [Fact]
    public void AHostWithoutApplicationInfoHasNoVersion() => Assert.Null( BackstageCommandFactory.GetVersion( null ) );
}
