// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Aspects.Introspection.Query.Commands;
using Spectre.Console;
using Spectre.Console.Cli;
using System.IO;
using System.Linq;

namespace SharpCrafters.Aspects.Introspection.Query.Tests.Commands;

/// <summary>
/// Runs the <c>introspect</c> commands in the process, as the command line of a tool would.
/// </summary>
internal static class CommandRunner
{
    public static (int ExitCode, string Output, string Error) Run(
        string workingDirectory,
        IntrospectionProvider provider,
        string[] arguments,
        bool isTerminal = false,
        string? noColor = null )
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var app = new CommandApp();

        app.Configure(
            c =>
            {
                c.ConfigureConsole( AnsiConsole.Create( new AnsiConsoleSettings { Out = new AnsiConsoleOutput( error ), Ansi = AnsiSupport.No } ) );

                IntrospectionCommandFactory.AddIntrospectBranch(
                    c,
                    new IntrospectionCommandOptions( provider, output, error, workingDirectory, isTerminal, n => n == "NO_COLOR" ? noColor : null ) );
            } );

        var exitCode = app.Run( new[] { "introspect" }.Concat( arguments ) );

        return (exitCode, output.ToString(), error.ToString());
    }
}
