// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Aspects.Introspection.Query.Commands;
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace SharpCrafters.Aspects.Introspection.Query.Tests.Commands;

/// <summary>
/// <c>introspect batch</c> runs several queries over one load of the files.
/// </summary>
public sealed class BatchCommandTests : IDisposable
{
    private readonly TestProject _project = new();
    private readonly TestProvider _provider;

    public BatchCommandTests()
    {
        this._provider = new TestProvider { HasLocations = true };
        this._provider.Locations["P:App.Customer.FullName"] = new IntrospectionSourceLocation( this._project.SourceFile, 18, 19 );
    }

    public void Dispose() => this._project.Dispose();

    private (int ExitCode, string Output, string Error) Run( string input, params string[] arguments )
        => CommandRunner.Run( this._project.Directory, this._provider, ["batch", .. arguments], input: input );

    private static JsonElement[] Results( string output ) => JsonDocument.Parse( output ).RootElement.GetProperty( "results" ).EnumerateArray().ToArray();

    [Fact]
    public void TheFilesAreLoadedOnceForSeveralQueries()
    {
        var (exitCode, output, error) = this.Run(
            """
            # Comments and blank lines are skipped.
            list aspect-classes

            list aspect-instances --where "aspectClass/shortName eq 'Log'" --count
            show declaration Customer.cs:18
            show aspect-class Log
            files
            """,
            "--format",
            "json" );

        Assert.True( exitCode == 0, error );
        Assert.Equal( 1, this._provider.LoadCount );

        var results = Results( output );
        Assert.Equal( 5, results.Length );
        Assert.All( results, r => Assert.Equal( 0, r.GetProperty( "exitCode" ).GetInt32() ) );
        Assert.Equal( "list aspect-classes", results[0].GetProperty( "query" ).GetString() );
        Assert.Equal( 3, results[0].GetProperty( "result" ).GetProperty( "count" ).GetInt32() );
        Assert.Equal( 3, results[1].GetProperty( "result" ).GetProperty( "count" ).GetInt32() );
        Assert.Equal( "P:App.Customer.FullName", results[2].GetProperty( "result" ).GetProperty( "declaration" ).GetProperty( "id" ).GetString() );

        // A command of the provider runs in the batch, over the same session.
        Assert.Equal( "App net10.0", results[4].GetProperty( "result" )[0].GetString() );
    }

    [Fact]
    public void AFailingQueryDoesNotStopTheNextOnes()
    {
        var (exitCode, output, _) = this.Run(
            """
            list nothings
            list aspect-classes --count
            lst aspect-classes
            """,
            "--format",
            "json" );

        Assert.Equal( IntrospectionExitCodes.InvalidQuery, exitCode );

        var results = Results( output );
        Assert.Equal( IntrospectionExitCodes.InvalidQuery, results[0].GetProperty( "exitCode" ).GetInt32() );
        Assert.Contains( "'nothings' is not a collection", results[0].GetProperty( "error" ).GetString(), StringComparison.Ordinal );
        Assert.Equal( 0, results[1].GetProperty( "exitCode" ).GetInt32() );
        Assert.Equal( IntrospectionExitCodes.InvalidQuery, results[2].GetProperty( "exitCode" ).GetInt32() );
        Assert.Contains( "lst", results[2].GetProperty( "error" ).GetString(), StringComparison.Ordinal );
    }

    [Fact]
    public void AQueryCannotSetTheScopeOrTheFormat()
    {
        var (exitCode, output, _) = this.Run(
            """
            list aspect-classes --format text
            list aspect-classes -f net48
            list aspect-classes --project=App.csproj
            batch
            list aspect-classes 'unclosed
            """,
            "--format",
            "json" );

        Assert.Equal( IntrospectionExitCodes.InvalidQuery, exitCode );

        var errors = Results( output ).Select( r => r.GetProperty( "error" ).GetString() ).ToArray();
        Assert.Contains( "cannot set --format", errors[0], StringComparison.Ordinal );
        Assert.Contains( "cannot set -f", errors[1], StringComparison.Ordinal );
        Assert.Contains( "cannot set --project", errors[2], StringComparison.Ordinal );
        Assert.Contains( "cannot run another batch", errors[3], StringComparison.Ordinal );
        Assert.Contains( "is not closed", errors[4], StringComparison.Ordinal );
    }

    [Fact]
    public void TheScopeOfTheBatchAppliesToEveryQuery()
    {
        using var project = new TestProject( targetFrameworks: "<TargetFrameworks>net10.0;net48</TargetFrameworks>" );
        project.WriteSymbols( "net48", TestProject.DefaultSymbols );

        var (exitCode, output, error) = CommandRunner.Run(
            project.Directory,
            this._provider,
            ["batch", "-f", "net48", "--format", "json"],
            input: "files" );

        Assert.True( exitCode == 0, error );
        Assert.Equal( "App net48", Results( output )[0].GetProperty( "result" )[0].GetString() );
    }

    [Fact]
    public void AFileGivesTheSameResultsAsTheStandardInput()
    {
        const string queries = "list aspect-classes\nshow aspect-class Log\n";
        var file = Path.Combine( this._project.Directory, "queries.txt" );
        File.WriteAllText( file, queries );

        var fromInput = this.Run( queries, "--format", "json" );
        var fromFile = CommandRunner.Run( this._project.Directory, this._provider, ["batch", "--file", "queries.txt", "--format", "json"] );

        Assert.Equal( 0, fromFile.ExitCode );
        Assert.Equal( fromInput.Output, fromFile.Output );
    }

    [Fact]
    public void ATerminalInputIsNotWaitedFor()
    {
        // No input: the standard input of the runner is a terminal.
        var (exitCode, _, error) = CommandRunner.Run( this._project.Directory, this._provider, ["batch"] );

        Assert.Equal( IntrospectionExitCodes.InvalidQuery, exitCode );
        Assert.Contains( "The standard input is a terminal", error, StringComparison.Ordinal );
    }

    [Fact]
    public void TheTextOutputNamesEachQuery()
    {
        var (exitCode, output, error) = this.Run( "list aspect-classes --count\nlist nothings" );

        Assert.Equal( IntrospectionExitCodes.InvalidQuery, exitCode );
        Assert.StartsWith( "> list aspect-classes --count" + Environment.NewLine + "3" + Environment.NewLine, output, StringComparison.Ordinal );
        Assert.Contains( "> list nothings" + Environment.NewLine + "error: 'nothings' is not a collection", output, StringComparison.Ordinal );
        Assert.Equal( "", error );
    }

    [Theory]
    [InlineData( "list aspect-instances", new[] { "list", "aspect-instances" } )]
    [InlineData( "  list   a  ", new[] { "list", "a" } )]
    [InlineData( "list --where \"name eq 'A b'\"", new[] { "list", "--where", "name eq 'A b'" } )]
    [InlineData( "list --where 'name eq \"x\"'", new[] { "list", "--where", "name eq \"x\"" } )]
    [InlineData( "a\"b c\"'d e'f", new[] { "ab cd ef" } )]
    [InlineData( "\"a \\\" b \\\\ c \\d\"", new[] { "a \" b \\ c \\d" } )]
    [InlineData( "show declaration 'C:\\src\\Customer.cs:18'", new[] { "show", "declaration", "C:\\src\\Customer.cs:18" } )]

    // Outside quotes, a backslash escapes a whitespace, a quote or a backslash, and is literal before another character.
    [InlineData( "show declaration src/My\\ File.cs:18", new[] { "show", "declaration", "src/My File.cs:18" } )]
    [InlineData( "a\\'b \\\"c \\\\d", new[] { "a'b", "\"c", "\\d" } )]
    [InlineData( "show declaration src\\Customer.cs:18", new[] { "show", "declaration", "src\\Customer.cs:18" } )]

    // Inside double quotes, $ and ` are escapes too, as in POSIX shells.
    [InlineData( "\"\\$x \\`1\"", new[] { "$x `1" } )]
    public void ALineIsSplitAsAShellSplitsIt( string line, string[] expected ) => Assert.Equal( expected, QueryLineSplitter.Split( line ) );

    [Theory]
    [InlineData( "list 'a" )]
    [InlineData( "list \"a" )]
    [InlineData( "list a\\" )]
    public void AnUnclosedQuoteOrADanglingEscapeIsRejected( string line ) => Assert.Throws<FormatException>( () => QueryLineSplitter.Split( line ) );

    [Fact]
    public void TheHelpOfACommandIsTheResultOfItsQuery()
    {
        var (exitCode, output, error) = this.Run( "list --help" );

        Assert.True( exitCode == 0, error );
        Assert.Contains( "--where", output, StringComparison.Ordinal );
    }
}
