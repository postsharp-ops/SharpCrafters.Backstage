// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using Spectre.Console;
using System.IO;

namespace SharpCrafters.Aspects.Introspection.Query.Commands;

/// <summary>
/// Writes the output and the errors of the <c>introspect</c> commands with the colors of Spectre.Console: the JSON with its
/// widget, an error in red and a warning in yellow. The colors are used when the stream is a terminal, unless <c>NO_COLOR</c>
/// is set, or as <c>--color</c> says.
/// </summary>
internal static class IntrospectionConsole
{
    /// <summary>
    /// Gets a value indicating whether a stream is colored, from <c>--color</c> (<c>auto</c>, <c>always</c> or <c>never</c>),
    /// <c>NO_COLOR</c>, and whether the stream is a terminal.
    /// </summary>
    public static bool IsColored( IntrospectionCommandOptions options, string? color, bool isTerminal )
        => color switch
        {
            "always" => true,
            "never" => false,
            _ => isTerminal && string.IsNullOrEmpty( options.GetEnvironmentVariable( "NO_COLOR" ) )
        };

    /// <summary>
    /// Creates a console that writes ANSI colors to a stream.
    /// </summary>
    public static IAnsiConsole Create( TextWriter writer )
    {
        var console = AnsiConsole.Create(
            new AnsiConsoleSettings
            {
                Out = new AnsiConsoleOutput( writer ),
                Ansi = AnsiSupport.Yes,

                // Detection finds no color on a redirected stream, which --color always colors.
                ColorSystem = ColorSystemSupport.Standard,
                Interactive = InteractionSupport.No
            } );

        // A line is never wrapped: a break inside a JSON string, or inside a path of a message, would break what is copied.
        console.Profile.Width = int.MaxValue / 2;

        return console;
    }

    /// <summary>
    /// Writes an error, in red, as <c>error: message</c>.
    /// </summary>
    public static void WriteError( IntrospectionCommandOptions options, string? color, string message ) => Write( options, color, "error", "red", message );

    /// <summary>
    /// Writes a warning, in yellow, as <c>warning: message</c>.
    /// </summary>
    public static void WriteWarning( IntrospectionCommandOptions options, string? color, string message ) => Write( options, color, "warning", "yellow", message );

    private static void Write( IntrospectionCommandOptions options, string? color, string prefix, string style, string message )
    {
        if ( IsColored( options, color, options.IsErrorTerminal ) )
        {
            // The message is escaped: a selector or a path can hold brackets, which markup reads as styles.
            Create( options.ErrorOutput ).MarkupLine( $"[{style}]{Markup.Escape( prefix + ": " + message )}[/]" );
        }
        else
        {
            options.ErrorOutput.WriteLine( prefix + ": " + message );
        }
    }
}
