// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Aspects.Introspection.Query.Output;
using Spectre.Console;
using Spectre.Console.Cli;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace SharpCrafters.Aspects.Introspection.Query.Commands;

/// <summary>
/// The settings of <c>introspect batch</c>.
/// </summary>
public sealed class BatchCommandSettings : IntrospectionCommandSettings
{
    /// <summary>
    /// Gets the file that holds the queries, or <c>null</c> or <c>-</c> to read them from the standard input.
    /// </summary>
    [Description( "The file that holds the queries, one per line. By default, or with -, the standard input." )]
    [CommandOption( "--file <PATH>" )]
    public string? File { get; init; }
}

/// <summary>
/// <c>introspect batch</c>: runs several queries, one per line of the standard input or of a file, over one evaluation of the projects
/// and one load of their files.
/// </summary>
/// <remarks>
/// <para>
/// A line is a command line without <c>introspect</c>, such as <c>show declaration src/Customer.cs:18</c>. Blank lines and lines
/// that start with <c>#</c> are skipped. The scope and the format are those of the batch, and a line cannot set them.
/// </para>
/// <para>
/// Each line runs through a command line of its own, with only the <c>introspect</c> branch, so that it is parsed and validated as
/// it is on the command line and the commands of the provider run too. Its options share the sessions of the batch
/// (<see cref="IntrospectionSessionCache"/>), which is what makes the batch cheaper than separate calls.
/// </para>
/// <para>
/// Every query runs, even after one fails. The exit code is that of the first query that failed, or 0.
/// </para>
/// </remarks>
public sealed class BatchCommand : IntrospectionCommand<BatchCommandSettings>
{
    // The options of a query that the batch sets for all of its queries.
    private static readonly string[] _batchOptions = ["--project", "--solution", "-f", "--framework", "-c", "--configuration", "--format", "--color", "--file"];

    /// <inheritdoc />
    protected override int Execute( IntrospectionCommandOptions options, BatchCommandSettings settings )
    {
        var queries = ReadInput( options, settings )
            .Split( '\n' )
            .Select( l => l.Trim() )
            .Where( l => l.Length > 0 && !l.StartsWith( '#' ) )
            .ToList();

        if ( queries.Count == 0 )
        {
            throw new IntrospectionCommandException( "The batch holds no query: write one command per line, such as 'list aspect-classes'.", IntrospectionExitCodes.InvalidQuery );
        }

        var sessions = new IntrospectionSessionCache();
        var scope = GetScopeArguments( settings );
        var results = queries.Select( q => Run( options, sessions, q, scope, settings.Format == "text" ? "text" : "json" ) ).ToList();

        switch ( settings.Format )
        {
            case "text":
                foreach ( var result in results )
                {
                    options.StandardOutput.WriteLine( "> " + result.Query );
                    options.StandardOutput.Write( result.ExitCode == 0 ? result.Output : "error: " + result.Error + Environment.NewLine );
                    options.StandardOutput.WriteLine();
                }

                break;

            case "jsonl":
                // The files that the queries read, once, on the first line.
                options.StandardOutput.WriteLine( JsonOutput.Write( w => WriteFiles( w, results ), indented: false ) );

                foreach ( var result in results )
                {
                    options.StandardOutput.WriteLine( JsonOutput.Write( w => WriteResult( w, result ), indented: false ) );
                }

                break;

            default:
                WriteJson(
                    options,
                    settings,
                    JsonOutput.Write(
                        w =>
                        {
                            w.WriteStartObject();

                            // The queries share one session, so they read the same files: they are written once, not in each result.
                            WriteFiles( w, results, inObject: true );
                            w.WriteStartArray( "results" );

                            foreach ( var result in results )
                            {
                                WriteResult( w, result );
                            }

                            w.WriteEndArray();
                            w.WriteEndObject();
                        } ) );

                break;
        }

        return results.Select( r => r.ExitCode ).FirstOrDefault( c => c != IntrospectionExitCodes.Success );
    }

    private static string ReadInput( IntrospectionCommandOptions options, BatchCommandSettings settings )
    {
        if ( settings.File != null && settings.File != "-" )
        {
            var path = Path.GetFullPath( Path.Combine( options.WorkingDirectory, settings.File ) );

            return System.IO.File.Exists( path )
                ? System.IO.File.ReadAllText( path )
                : throw new IntrospectionCommandException( $"The file '{settings.File}' does not exist.", IntrospectionExitCodes.Error );
        }

        if ( options.IsInputTerminal )
        {
            // Without this, the command would wait for input that an agent never sends.
            throw new IntrospectionCommandException(
                "The batch reads its queries, one per line, from the standard input or from --file. The standard input is a terminal: pipe the queries, for example with a heredoc, or give --file.",
                IntrospectionExitCodes.InvalidQuery );
        }

        return options.StandardInput.ReadToEnd();
    }

    private static List<string> GetScopeArguments( IntrospectionCommandSettings settings )
    {
        var arguments = new List<string>();

        void Add( string name, string? value )
        {
            if ( value != null )
            {
                arguments.Add( name );
                arguments.Add( value );
            }
        }

        Add( "--project", settings.Project );
        Add( "--solution", settings.Solution );
        Add( "--framework", settings.Framework );
        Add( "--configuration", settings.Configuration );

        return arguments;
    }

    private static QueryResult Run( IntrospectionCommandOptions options, IntrospectionSessionCache sessions, string query, List<string> scope, string format )
    {
        string[] words;

        try
        {
            words = QueryLineSplitter.Split( query );
        }
        catch ( FormatException e )
        {
            return new QueryResult( query, IntrospectionExitCodes.InvalidQuery, "", e.Message );
        }

        if ( words.Length > 0 && words[0] == "batch" )
        {
            return new QueryResult( query, IntrospectionExitCodes.InvalidQuery, "", "A batch cannot run another batch." );
        }

        var batchOption = words.FirstOrDefault( w => _batchOptions.Any( o => w == o || w.StartsWith( o + "=", StringComparison.Ordinal ) ) );

        if ( batchOption != null )
        {
            return new QueryResult(
                query,
                IntrospectionExitCodes.InvalidQuery,
                "",
                $"A query of a batch cannot set {batchOption.Split( '=' )[0]}: the scope and the format are those of the batch." );
        }

        var output = new StringWriter();
        var error = new StringWriter();
        var app = new CommandApp();

        app.Configure(
            c =>
            {
                // The messages of Spectre, such as a parse error or the help of a command, go to the error of the query.
                c.ConfigureConsole( AnsiConsole.Create( new AnsiConsoleSettings { Out = new AnsiConsoleOutput( error ), Ansi = AnsiSupport.No } ) );
                c.PropagateExceptions();
                IntrospectionCommandFactory.AddIntrospectBranch( c, options.ForQuery( output, error, sessions ) );
            } );

        int exitCode;

        try
        {
            exitCode = app.Run( ["introspect", .. words, .. scope, "--format", format, "--color", "never"] );
        }
        catch ( CommandAppException e )
        {
            // An unknown command or option, a missing argument, or an invalid value.
            error.WriteLine( "error: " + e.Message );
            exitCode = IntrospectionExitCodes.InvalidQuery;
        }

        var errorText = error.ToString();

        if ( exitCode == IntrospectionExitCodes.Success )
        {
            // The warnings, such as about a stale file, which the first query that opens the scope writes.
            options.ErrorOutput.Write( errorText );

            return new QueryResult( query, exitCode, output.ToString(), "" );
        }

        var message = string.Join(
            Environment.NewLine,
            errorText.Split( '\n' ).Select( l => l.TrimEnd( '\r' ) ).Where( l => l.Length > 0 ).Select( l => l.StartsWith( "error: ", StringComparison.Ordinal ) ? l[7..] : l ) );

        return new QueryResult( query, exitCode, output.ToString(), message );
    }

    private static void WriteResult( Utf8JsonWriter writer, QueryResult result )
    {
        writer.WriteStartObject();
        writer.WriteString( "query", result.Query );
        writer.WriteNumber( "exitCode", result.ExitCode );

        if ( result.ExitCode != IntrospectionExitCodes.Success )
        {
            writer.WriteString( "error", result.Error );
        }
        else if ( TryParse( result.Output ) is { } document )
        {
            // Written again rather than copied, so that the result follows the indentation of the batch, and without its files, which the
            // batch writes once.
            using ( document )
            {
                writer.WritePropertyName( "result" );

                if ( document.RootElement.ValueKind == JsonValueKind.Object )
                {
                    writer.WriteStartObject();

                    foreach ( var property in document.RootElement.EnumerateObject() )
                    {
                        if ( property.Name != "files" )
                        {
                            property.WriteTo( writer );
                        }
                    }

                    writer.WriteEndObject();
                }
                else
                {
                    document.RootElement.WriteTo( writer );
                }
            }
        }
        else
        {
            // The help of a command, which is text.
            writer.WriteString( "output", result.Output );
        }

        writer.WriteEndObject();
    }

    // The union of the files of the results, in the order in which the queries read them.
    private static void WriteFiles( Utf8JsonWriter writer, IEnumerable<QueryResult> results, bool inObject = false )
    {
        var files = new List<string>();

        foreach ( var result in results.Where( r => r.ExitCode == IntrospectionExitCodes.Success ) )
        {
            using var document = TryParse( result.Output );

            if ( document is { RootElement.ValueKind: JsonValueKind.Object } && document.RootElement.TryGetProperty( "files", out var list ) )
            {
                foreach ( var file in list.EnumerateArray() )
                {
                    var text = file.GetRawText();

                    if ( !files.Contains( text ) )
                    {
                        files.Add( text );
                    }
                }
            }
        }

        if ( !inObject )
        {
            writer.WriteStartObject();
        }

        writer.WriteStartArray( "files" );

        foreach ( var file in files )
        {
            using var document = JsonDocument.Parse( file );
            document.RootElement.WriteTo( writer );
        }

        writer.WriteEndArray();

        if ( !inObject )
        {
            writer.WriteEndObject();
        }
    }

    private static JsonDocument? TryParse( string json )
    {
        try
        {
            return json.Length == 0 ? null : JsonDocument.Parse( json );
        }
        catch ( JsonException )
        {
            return null;
        }
    }

    private sealed record QueryResult( string Query, int ExitCode, string Output, string Error );
}

/// <summary>
/// Splits a line of a batch into words, as a POSIX shell does: words are separated by whitespace, <c>'…'</c> is literal, and inside
/// <c>"…"</c>, <c>\"</c> and <c>\\</c> are escapes. A quoted part can be joined to other text in the same word.
/// </summary>
internal static class QueryLineSplitter
{
    /// <exception cref="FormatException">A quote is not closed.</exception>
    public static string[] Split( string line )
    {
        var words = new List<string>();
        var word = new StringBuilder();
        var inWord = false;

        for ( var i = 0; i < line.Length; i++ )
        {
            var c = line[i];

            if ( char.IsWhiteSpace( c ) )
            {
                if ( inWord )
                {
                    words.Add( word.ToString() );
                    word.Clear();
                    inWord = false;
                }

                continue;
            }

            inWord = true;

            switch ( c )
            {
                case '\'':
                    var end = line.IndexOf( '\'', i + 1 );

                    if ( end < 0 )
                    {
                        throw new FormatException( $"The quote at column {i + 1} is not closed." );
                    }

                    word.Append( line, i + 1, end - i - 1 );
                    i = end;

                    break;

                case '"':
                    var start = i;

                    for ( i++; ; i++ )
                    {
                        if ( i >= line.Length )
                        {
                            throw new FormatException( $"The quote at column {start + 1} is not closed." );
                        }

                        if ( line[i] == '"' )
                        {
                            break;
                        }

                        if ( line[i] == '\\' && i + 1 < line.Length && line[i + 1] is '"' or '\\' )
                        {
                            i++;
                        }

                        word.Append( line[i] );
                    }

                    break;

                default:
                    word.Append( c );

                    break;
            }
        }

        if ( inWord )
        {
            words.Add( word.ToString() );
        }

        return words.ToArray();
    }
}
