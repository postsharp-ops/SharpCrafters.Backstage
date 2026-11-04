// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Aspects.Introspection.Query.Projects;
using Spectre.Console;
using Spectre.Console.Cli;
using Spectre.Console.Json;
using System.Collections.Generic;
using System.Threading;

namespace SharpCrafters.Aspects.Introspection.Query.Commands;

/// <summary>
/// The base of the <c>introspect</c> commands: reports an <see cref="IntrospectionCommandException"/> as an error with its exit
/// code.
/// </summary>
/// <typeparam name="T">The settings.</typeparam>
public abstract class IntrospectionCommand<T> : Command<T>
    where T : IntrospectionCommandSettings
{
    /// <inheritdoc />
    protected sealed override int Execute( CommandContext context, T settings, CancellationToken cancellationToken )
    {
        var options = (IntrospectionCommandOptions) context.Data!;

        try
        {
            return this.Execute( options, settings );
        }
        catch ( IntrospectionCommandException e )
        {
            IntrospectionConsole.WriteError( options, settings.Color, e.Message );

            return e.ExitCode;
        }
    }

    /// <summary>
    /// Runs the command.
    /// </summary>
    protected abstract int Execute( IntrospectionCommandOptions options, T settings );

    /// <summary>
    /// Gets the targets of the scope of a command whose file exists, as <c>list</c> and <c>show</c> read them: the scope rules, the
    /// evaluation of the projects, and the warnings about missing and stale files. A command that the provider of a product adds
    /// uses it to read the files of the scope itself, such as <c>metrics</c>, which reads what the common model leaves out.
    /// </summary>
    /// <exception cref="IntrospectionCommandException">No file exists, or the scope is invalid.</exception>
    protected static IReadOnlyList<IntrospectionTarget> GetTargets( IntrospectionCommandOptions options, IntrospectionCommandSettings settings )
        => IntrospectionSession.GetTargets( options, settings );

    /// <summary>
    /// Gets the model of one target of <see cref="GetTargets"/>, before the merge with the other targets, from the index of its file.
    /// In a batch, it is the model that the other queries read.
    /// </summary>
    /// <exception cref="IntrospectionCommandException">The file is malformed.</exception>
    protected static IntrospectionModel GetModel( IntrospectionCommandOptions options, IntrospectionCommandSettings settings, IntrospectionTarget target )
        => IntrospectionSession.GetModel( options, settings, target );

    /// <summary>
    /// Writes a JSON document, colored with the JSON widget of Spectre.Console when the output is a terminal, unless
    /// <c>NO_COLOR</c> is set or <c>--color</c> says otherwise. The JSON never goes through markup, which reads brackets as styles.
    /// </summary>
    protected static void WriteJson( IntrospectionCommandOptions options, IntrospectionCommandSettings settings, string json )
    {
        if ( IntrospectionConsole.IsColored( options, settings.Color, options.IsOutputTerminal ) && settings.Format == "json" )
        {
            var console = IntrospectionConsole.Create( options.StandardOutput );
            console.Write( new JsonText( json ) );
            console.WriteLine();
        }
        else
        {
            options.StandardOutput.WriteLine( json );
        }
    }
}
