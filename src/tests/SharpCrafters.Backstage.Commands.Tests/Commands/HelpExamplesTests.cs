// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using Metalama.Backstage;
using Spectre.Console;
using Spectre.Console.Cli;
using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace SharpCrafters.Backstage.Commands.Tests.Commands;

/// <summary>
/// Tests the examples that the help of the commands shows.
/// </summary>
public sealed class HelpExamplesTests : CommandsTestsBase
{
    public HelpExamplesTests( ITestOutputHelper logger ) : base( logger ) { }

    /// <summary>
    /// Tests that the root help shows the root examples of Backstage and the ones that the host adds after them. When the
    /// root command has no example, Spectre.Console.Cli shows the examples of the first subcommands that it finds instead.
    /// </summary>
    [Fact]
    public async Task RootHelpShowsRootExamples()
    {
        var output = await this.GetHelpAsync( ["--help"], config => config.AddExample( "news", "status" ) );

        Assert.Contains( "license register <license-key>", output, StringComparison.Ordinal );
        Assert.Contains( "config edit diagnostics", output, StringComparison.Ordinal );
        Assert.Contains( "telemetry disable", output, StringComparison.Ordinal );
        Assert.Contains( "cleanup", output, StringComparison.Ordinal );
        Assert.Contains( "news status", output, StringComparison.Ordinal );

        // An example of a subcommand that is not a root example.
        Assert.DoesNotContain( "license acquire-lease", output, StringComparison.Ordinal );
    }

    /// <summary>
    /// Tests that the help of a command shows its examples.
    /// </summary>
    [Theory]
    [InlineData( "license register", "license register https://license-server.example.com" )]
    [InlineData( "license community", "license community" )]
    [InlineData( "config dump", "config dump licensing" )]
    [InlineData( "telemetry upload", "telemetry upload --async" )]
    [InlineData( "shutdown", "shutdown --force --timeout 30" )]
    [InlineData( "news status", "news status" )]
    public async Task CommandHelpShowsExamples( string command, string expectedExample )
    {
        var output = await this.GetHelpAsync( [..command.Split( ' ' ), "--help"] );

        Assert.Contains( "EXAMPLES:", output, StringComparison.Ordinal );
        Assert.Contains( expectedExample, output, StringComparison.Ordinal );
    }

    /// <summary>
    /// Runs the help with <see cref="ConfiguratorExtensions.ValidateExamples"/>, so that Spectre.Console.Cli parses
    /// every example of every command and fails when one of them does not match the arguments of its command.
    /// </summary>
    private async Task<string> GetHelpAsync( string[] args, Action<IConfigurator>? configureMoreCommands = null )
    {
        var output = new StringWriter();

        var console = AnsiConsole.Create(
            new AnsiConsoleSettings { Ansi = AnsiSupport.No, ColorSystem = ColorSystemSupport.NoColors, Out = new AnsiConsoleOutput( output ) } );

        console.Profile.Width = 500;

        var app = new CommandApp();

        BackstageCommandFactory.ConfigureCommandApp(
            app,
            new BackstageCommandOptions( this, MetalamaProduct.Instance, output, output, AnsiSupport.No ),
            config =>
            {
                config.ConfigureConsole( console );
                config.ValidateExamples();
                config.PropagateExceptions();
                configureMoreCommands?.Invoke( config );
            } );

        var exitCode = await app.RunAsync( args );

        Assert.Equal( 0, exitCode );

        return output.ToString();
    }
}
