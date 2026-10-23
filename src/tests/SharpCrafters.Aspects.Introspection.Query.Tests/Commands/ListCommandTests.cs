// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace SharpCrafters.Aspects.Introspection.Query.Tests.Commands;

/// <summary>
/// <c>introspect list</c>, on a project that is evaluated with the MSBuild of the .NET SDK and never built.
/// </summary>
public sealed class ListCommandTests : IDisposable
{
    private readonly TestProject _project = new();
    private readonly TestProvider _provider = new();

    public void Dispose() => this._project.Dispose();

    private (int ExitCode, string Output, string Error) Run( params string[] arguments ) => CommandRunner.Run( this._project.Directory, this._provider, arguments );

    private JsonElement RunJson( params string[] arguments )
    {
        var (exitCode, output, error) = this.Run( arguments.Concat( ["--format", "json"] ).ToArray() );
        Assert.True( exitCode == 0, error );

        return JsonDocument.Parse( output ).RootElement;
    }

    [Fact]
    public void TheAspectInstancesAreListedWithTheFileTheyComeFrom()
    {
        var result = this.RunJson( "list", "aspect-instances" );

        Assert.Equal( 5, result.GetProperty( "count" ).GetInt32() );

        var file = Assert.Single( result.GetProperty( "files" ).EnumerateArray() );
        Assert.Equal( "App", file.GetProperty( "project" ).GetString() );
        Assert.Equal( "net10.0", file.GetProperty( "targetFramework" ).GetString() );
        Assert.Equal( "Debug", file.GetProperty( "configuration" ).GetString() );
        Assert.Equal( "bin/Debug/net10.0/App.pssym", file.GetProperty( "path" ).GetString() );
        Assert.False( file.GetProperty( "stale" ).GetBoolean() );

        var first = result.GetProperty( "items" )[0];
        Assert.Matches( "^[0-9a-f]{12}$", first.GetProperty( "id" ).GetString() );
        Assert.Equal( "NotifyPropertyChanged", first.GetProperty( "aspectClass" ).GetProperty( "shortName" ).GetString() );
        Assert.Equal( "T:App.Customer", first.GetProperty( "targetDeclaration" ).GetProperty( "id" ).GetString() );
        Assert.Equal( "App", first.GetProperty( "project" ).GetString() );

        // A list writes no list of objects; show does.
        Assert.False( first.TryGetProperty( "transformations", out _ ) );

        // A field that the product does not record is not written.
        Assert.False( first.TryGetProperty( "isSkipped", out _ ) );
    }

    [Theory]
    [InlineData( "aspectClass/shortName eq 'Log'", 3 )]
    [InlineData( "aspectClass/shortName ne 'Log' and linesOfCodeAvoided eq 0", 2 )]
    [InlineData( "transformations/$count gt 1", 1 )]
    [InlineData( "transformations/any(t: t/semantic eq 'Getter')", 1 )]
    [InlineData( "transformations/all(t: t/description ne null)", 2 )]
    [InlineData( "predecessors/any(p: p/kind eq 'Multicast')", 2 )]
    [InlineData( "tags/any(t: t eq 'premium')", 1 )]
    [InlineData( "aspectClass/shortName in ('Cache', 'NotifyPropertyChanged')", 2 )]
    [InlineData( "startswith(targetDeclaration/id, 'M:App.Repository.') and not (aspectClass/shortName eq 'Cache')", 2 )]
    [InlineData( "contains(tolower(aspectClass/name), 'notify')", 1 )]
    [InlineData( "matchesPattern(targetDeclaration/id, '^M:.*Get')", 2 )]
    [InlineData( "targetDeclaration/declaringType/id eq 'T:App.Repository'", 3 )]
    public void TheConditionsFilterTheObjects( string condition, int count )
        => Assert.Equal( count, this.RunJson( "list", "aspect-instances", "--where", condition ).GetProperty( "count" ).GetInt32() );

    [Fact]
    public void SeveralConditionsAreCombinedWithAnd()
        => Assert.Equal(
            1,
            this.RunJson( "list", "aspect-instances", "--where", "aspectClass/shortName eq 'Log'", "--where", "linesOfCodeAvoided gt 0" )
                .GetProperty( "count" )
                .GetInt32() );

    [Fact]
    public void TheSelectionIsWrittenNestedAsItsPaths()
    {
        var item = this.RunJson( "list", "aspect-instances", "--select", "id,aspectClass/shortName,transformations/$count", "--limit", "1" )
            .GetProperty( "items" )
            .EnumerateArray()
            .Single();

        Assert.Equal( ["id", "aspectClass", "transformations/$count"], item.EnumerateObject().Select( p => p.Name ) );
        Assert.Equal( "NotifyPropertyChanged", item.GetProperty( "aspectClass" ).GetProperty( "shortName" ).GetString() );
        Assert.Equal( 2, item.GetProperty( "transformations/$count" ).GetInt32() );
    }

    [Fact]
    public void TheOrderSkipAndLimitAreApplied()
    {
        var items = this.RunJson( "list", "transformations", "--order-by", "order desc", "--skip", "1", "--limit", "3", "--select", "order" )
            .GetProperty( "items" )
            .EnumerateArray()
            .Select( i => i.GetProperty( "order" ).GetInt32() );

        Assert.Equal( [5, 4, 3], items );
    }

    [Fact]
    public void TheCountAloneIsWritten()
    {
        var (exitCode, output, _) = this.Run( "list", "aspect-instances", "--count" );

        Assert.Equal( 0, exitCode );
        Assert.Equal( "5", output.Trim() );
    }

    [Fact]
    public void JsonLinesWriteOneObjectPerLine()
    {
        var (exitCode, output, _) = this.Run( "list", "aspect-classes", "--format", "jsonl" );

        Assert.Equal( 0, exitCode );

        var lines = output.Split( '\n', StringSplitOptions.RemoveEmptyEntries );
        Assert.Equal( 3, lines.Length );
        Assert.All( lines, l => Assert.Equal( JsonValueKind.Object, JsonDocument.Parse( l ).RootElement.ValueKind ) );
    }

    [Fact]
    public void TheTextOutputIsATableOfTheDefaultColumns()
    {
        var (exitCode, output, _) = this.Run( "list", "aspect-classes" );

        Assert.Equal( 0, exitCode );

        var lines = output.Split( Environment.NewLine );
        Assert.StartsWith( "shortName", lines[0], StringComparison.Ordinal );
        Assert.Contains( "aspectInstances/$count", lines[0], StringComparison.Ordinal );
        Assert.Contains( lines, l => l.StartsWith( "Log ", StringComparison.Ordinal ) && l.TrimEnd().EndsWith( '3' ) );
        Assert.Contains( lines, l => l.StartsWith( "From bin/Debug/net10.0/App.pssym (App, net10.0, Debug)", StringComparison.Ordinal ) );
    }

    [Fact]
    public void TheDeclarationsAreListedWithTheirRelations()
    {
        var items = this.RunJson( "list", "declarations", "--where", "aspectInstances/any() or transformations/any()", "--select", "id" )
            .GetProperty( "items" )
            .EnumerateArray()
            .Select( i => i.GetProperty( "id" ).GetString() )
            .ToList();

        Assert.Contains( "M:App.Customer.get_FullName", items );
        Assert.Contains( "M:App.Repository.Save(App.Customer)", items );

        // The original field of PostSharp maps back to the backing field of the source.
        Assert.DoesNotContain( items, i => i!.Contains( "k__OriginalField", StringComparison.Ordinal ) );
    }

    [Theory]
    [InlineData( "aspectclass/shortName eq 'Log'", "did you mean 'aspectClass'?" )]
    [InlineData( "foo eq 1", "The fields of aspectInstance are: id, key," )]
    [InlineData( "predecessors/any(p: p/kind eq 'Multicst')", "'Multicst' is not a value of 'p/kind'. The values are: Attribute, Multicast," )]
    [InlineData( "linesOfCodeAvoided eq 'two'", "A integer cannot be compared with a string" )]
    [InlineData( "transformations/ gt 1", "the shell removed it" )]
    [InlineData( "aspectClass eq 'Log'", "cannot be compared" )]
    [InlineData( "id eq 'a", "The string is not closed." )]
    [InlineData( "linesOfCodeAvoided", "is a integer, not a Boolean" )]
    [InlineData( "isSkipped eq true", "The files of TestProduct do not record 'isSkipped' of aspectInstance." )]
    public void AnInvalidConditionIsAnErrorWithItsPosition( string condition, string message )
    {
        var (exitCode, _, error) = this.Run( "list", "aspect-instances", "--where", condition );

        Assert.Equal( IntrospectionExitCodes.InvalidQuery, exitCode );
        Assert.Contains( message, error, StringComparison.Ordinal );
        Assert.Contains( "^", error, StringComparison.Ordinal );
    }

    [Fact]
    public void ACollectionThatTheProductDoesNotRecordIsAnError()
    {
        var (exitCode, _, error) = this.Run( "list", "diagnostics" );

        Assert.Equal( IntrospectionExitCodes.InvalidQuery, exitCode );
        Assert.Contains( "The files of TestProduct do not record diagnostics.", error, StringComparison.Ordinal );
    }

    [Fact]
    public void AnUnknownCollectionListsTheCollections()
    {
        var (exitCode, _, error) = this.Run( "list", "aspects" );

        Assert.Equal( IntrospectionExitCodes.InvalidQuery, exitCode );
        Assert.Contains( "aspect-classes, aspect-instances, predecessors", error, StringComparison.Ordinal );
    }

    [Fact]
    public void AStaleFileIsReported()
    {
        File.SetLastWriteTimeUtc( this._project.SourceFile, DateTime.UtcNow.AddMinutes( 10 ) );

        var (exitCode, output, error) = this.Run( "list", "aspect-classes", "--format", "json" );

        Assert.Equal( 0, exitCode );
        Assert.True( JsonDocument.Parse( output ).RootElement.GetProperty( "files" )[0].GetProperty( "stale" ).GetBoolean() );
        Assert.Contains( "may not describe the current source", error, StringComparison.Ordinal );
    }

    [Fact]
    public void TheJsonIsColoredOnATerminalOnly()
    {
        const string escape = "\u001b[";

        Assert.Contains( escape, CommandRunner.Run( this._project.Directory, this._provider, ["schema", "--format", "json"], isTerminal: true ).Output, StringComparison.Ordinal );
        Assert.DoesNotContain( escape, CommandRunner.Run( this._project.Directory, this._provider, ["schema", "--format", "json"] ).Output, StringComparison.Ordinal );
        Assert.DoesNotContain( escape, CommandRunner.Run( this._project.Directory, this._provider, ["schema", "--format", "json"], isTerminal: true, noColor: "1" ).Output, StringComparison.Ordinal );
        Assert.Contains( escape, CommandRunner.Run( this._project.Directory, this._provider, ["schema", "--format", "json", "--color", "always"] ).Output, StringComparison.Ordinal );

        // A colored document is still the same JSON once the escape sequences are removed.
        var colored = CommandRunner.Run( this._project.Directory, this._provider, ["schema", "--format", "json", "--color", "always"] ).Output;
        var plain = System.Text.RegularExpressions.Regex.Replace( colored, @"\u001b\[[0-9;]*m", "" );
        Assert.Equal( "TestProduct", JsonDocument.Parse( plain ).RootElement.GetProperty( "product" ).GetString() );
    }
}
