// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using JetBrains.Annotations;
using Metalama.Backstage;
using SharpCrafters.Backstage.Application;
using SharpCrafters.Backstage.Diagnostics;
using SharpCrafters.Backstage.Testing;
using Spectre.Console;
using Spectre.Console.Cli;
using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace SharpCrafters.Backstage.Commands.Tests.Commands
{
    [PublicAPI]
    public abstract class CommandsTestsBase : TestsBase, ICommandServiceProviderProvider
    {
        private readonly ILogger _logger;

        protected CommandsTestsBase( ITestOutputHelper logger, IApplicationInfo? applicationInfo = null )
            : base( logger, applicationInfo )
        {
            this._logger = this.ServiceProvider.GetLoggerFactory().GetLogger( "Console" );
        }

        protected Task TestCommandAsync(
            string commandLine,
            string? expectedOutput = null,
            string? unexpectedOutput = null,
            int expectedExitCode = 0 )
            => this.TestCommandAsync( commandLine.Split( ' ' ), expectedOutput, unexpectedOutput, expectedExitCode );

        protected async Task TestCommandAsync(
            string[] commandLine,
            string? expectedOutput = null,
            string? unexpectedOutput = null,
            int expectedExitCode = 0 )
        {
            var result = await this.RunAsync( commandLine );

            // Both streams, because a test of this kind is looking for something the command said and does not care
            // whether it said it to the console or to the log.
            var outputString = result.Console + result.Log;

            if ( expectedOutput != null )
            {
                Assert.Contains( expectedOutput, outputString, StringComparison.OrdinalIgnoreCase );
            }

            if ( unexpectedOutput != null )
            {
                Assert.DoesNotContain( unexpectedOutput, outputString, StringComparison.OrdinalIgnoreCase );
            }

            Assert.Equal( expectedExitCode, result.ExitCode );
        }

        /// <summary>
        /// Runs a command and returns what it wrote to the console, without what it wrote to the log, for a command
        /// whose output is a document that a test has to read rather than search for words in.
        /// </summary>
        /// <remarks>
        /// The log is left out because it is not part of the output: a trace line in front of a JSON document makes
        /// the document unreadable, and whether anything is traced at all depends on the diagnostics settings rather
        /// than on the command.
        /// </remarks>
        protected Task<string> GetCommandOutputAsync( string commandLine, int expectedExitCode = 0 )
            => this.GetCommandOutputAsync( commandLine.Split( ' ' ), expectedExitCode );

        /// <inheritdoc cref="GetCommandOutputAsync(string,int)"/>
        protected async Task<string> GetCommandOutputAsync( string[] commandLine, int expectedExitCode = 0 )
        {
            var result = await this.RunAsync( commandLine );

            Assert.Equal( expectedExitCode, result.ExitCode );

            return result.Console;
        }

        private async Task<(string Console, string Log, int ExitCode)> RunAsync( string[] commandLine )
        {
            var console = new StringWriter();
            var log = new StringWriter();

            this.Log.MessageReported += log.WriteLine;

            this._logger.Trace?.Log( $">> {string.Join( " ", commandLine )}" );

            var commandApp = new CommandApp();

            BackstageCommandFactory.ConfigureCommandApp(
                commandApp,
                new BackstageCommandOptions( this, MetalamaProduct.Instance, console, console, AnsiSupport.No ) );

            var exitCode = await commandApp.RunAsync( commandLine );

            var result = (console.ToString(), log.ToString(), exitCode);

            this.Log.Clear();

            return result;
        }

        IServiceProvider ICommandServiceProviderProvider.GetServiceProvider( CommandServiceProviderArgs args ) => this.ServiceProvider;
    }
}