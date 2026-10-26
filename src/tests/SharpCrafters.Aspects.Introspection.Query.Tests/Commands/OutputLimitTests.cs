// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Aspects.Introspection.Query.Commands;
using SharpCrafters.Aspects.Introspection.Query.Output;
using System;
using System.Text.Json;
using Xunit;

namespace SharpCrafters.Aspects.Introspection.Query.Tests.Commands;

/// <summary>
/// The lists of the output are limited, so that it stays readable by an agent, and say what they left out.
/// </summary>
public sealed class OutputLimitTests : IDisposable
{
    private readonly TestProject _project = new();
    private readonly TestProvider _provider = new();

    public void Dispose() => this._project.Dispose();

    private (int ExitCode, string Output, string Error) Run( params string[] arguments ) => CommandRunner.Run( this._project.Directory, this._provider, arguments );

    private JsonElement RunJson( params string[] arguments )
    {
        var (exitCode, output, error) = this.Run( [.. arguments, "--format", "json"] );
        Assert.True( exitCode == 0, error );

        return JsonDocument.Parse( output ).RootElement;
    }

    [Fact]
    public void ATruncatedListOfShowSaysHowManyObjectsItHasAndHowToListThem()
    {
        var instances = this.RunJson( "show", "aspect-class", "Log", "--limit", "1" ).GetProperty( "aspectClass" ).GetProperty( "aspectInstances" );

        Assert.Equal( 3, instances.GetProperty( "count" ).GetInt32() );
        Assert.True( instances.GetProperty( "truncated" ).GetBoolean() );
        Assert.Equal( 1, instances.GetProperty( "items" ).GetArrayLength() );

        var byNamespace = Assert.Single( instances.GetProperty( "byNamespace" ).EnumerateArray() );
        Assert.Equal( "App", byNamespace.GetProperty( "namespace" ).GetString() );
        Assert.Equal( 3, byNamespace.GetProperty( "count" ).GetInt32() );

        // The command of next lists every object of the list.
        var next = instances.GetProperty( "next" ).GetString()!;
        Assert.Equal( "list aspect-instances --where \"aspectClass/name eq 'App.LogAttribute'\" --all", next );
        Assert.Equal( 3, this.RunJson( QueryLineSplitter.Split( next ) ).GetProperty( "count" ).GetInt32() );
    }

    [Fact]
    public void TheRelatedObjectsOfShowAreSummaryRows()
    {
        var item = this.RunJson( "show", "aspect-class", "Log" ).GetProperty( "aspectClass" ).GetProperty( "aspectInstances" ).GetProperty( "items" )[0];

        // The fields of the summary, and not the lists or the other fields of the aspect instance.
        Assert.True( item.TryGetProperty( "id", out _ ) );
        Assert.True( item.TryGetProperty( "targetDeclaration", out _ ) );
        Assert.True( item.TryGetProperty( "project", out _ ) );
        Assert.False( item.TryGetProperty( "transformations", out _ ) );
        Assert.False( item.TryGetProperty( "linesOfCodeAvoided", out _ ) );
    }

    [Fact]
    public void AllWritesEveryRelatedObject()
    {
        var instances = this.RunJson( "show", "aspect-class", "Log", "--all" ).GetProperty( "aspectClass" ).GetProperty( "aspectInstances" );

        Assert.False( instances.GetProperty( "truncated" ).GetBoolean() );
        Assert.Equal( 3, instances.GetProperty( "items" ).GetArrayLength() );
        Assert.False( instances.TryGetProperty( "next", out _ ) );
    }

    [Theory]
    [InlineData( "show", "aspect-class", "Log" )]
    [InlineData( "list", "aspect-instances" )]
    public void LimitAndAllCannotBothBeGiven( params string[] command )
    {
        var (exitCode, _, error) = this.Run( [.. command, "--limit", "1", "--all"] );

        Assert.NotEqual( 0, exitCode );
        Assert.Contains( "--all and --limit cannot both be given", error, StringComparison.Ordinal );
    }

    [Fact]
    public void TheTextOutputSaysWhatItLeftOut()
    {
        var (exitCode, output, error) = this.Run( "show", "aspect-class", "Log", "--limit", "1", "--format", "text" );

        Assert.True( exitCode == 0, error );
        Assert.Contains(
            "... 2 more aspect instances, 3 in all, in 1 namespace(s): App (3). To see them, add --all, or run: introspect list aspect-instances --where \"aspectClass/name eq 'App.LogAttribute'\" --all",
            output,
            StringComparison.Ordinal );
    }

    [Fact]
    public void TheLimitAppliesToThePredecessorsInText()
    {
        var id = this.RunJson( "list", "aspect-instances", "--where", "aspectClass/shortName eq 'Log'", "--limit", "1" ).GetProperty( "items" )[0].GetProperty( "id" ).GetString()!;

        var (exitCode, output, error) = this.Run( "show", "aspect-instance", id, "--limit", "0", "--format", "text" );

        Assert.True( exitCode == 0, error );
        Assert.Contains( "... 1 more predecessors, 1 in all", output, StringComparison.Ordinal );
        Assert.Contains( "--where \"aspectInstances/any(a: a/id eq '" + id + "')\" --all", output, StringComparison.Ordinal );
    }

    [Fact]
    public void TheCommandOfAGenericDeclarationIsQuotedForBashAndBatch()
    {
        // Two aspect instances on a method of a generic type, whose identifier has a backtick.
        using var project = new TestProject(
            $$"""
             <Symbols xmlns="{{TestProject.SymbolsNamespace}}">
               <Class Class="T:[App]App.LogAttribute"><Instance Declaration="M:[App]App.Box{T}::Get()"><Target><JoinPoint /></Target></Instance></Class>
               <Class Class="T:[App]App.CacheAttribute"><Instance Declaration="M:[App]App.Box{T}::Get()"><Target><JoinPoint /></Target></Instance></Class>
             </Symbols>
             """ );

        var (exitCode, output, error) = CommandRunner.Run( project.Directory, this._provider, ["show", "declaration", "Box.Get", "--limit", "1", "--format", "json"] );
        Assert.True( exitCode == 0, error );

        // Single quotes: bash substitutes a backtick inside double quotes.
        var next = JsonDocument.Parse( output ).RootElement.GetProperty( "aspectInstances" ).GetProperty( "next" ).GetString()!;
        Assert.Equal( "show declaration '[App]M:App.Box`1.Get' --all", next );

        (exitCode, output, error) = CommandRunner.Run( project.Directory, this._provider, [.. QueryLineSplitter.Split( next ), "--format", "json"] );
        Assert.True( exitCode == 0, error );
        Assert.Equal( 2, JsonDocument.Parse( output ).RootElement.GetProperty( "aspectInstances" ).GetProperty( "items" ).GetArrayLength() );

        (exitCode, _, error) = CommandRunner.Run( project.Directory, this._provider, ["batch"], input: next );
        Assert.True( exitCode == 0, error );
    }

    [Theory]
    [InlineData( "[App]M:App.Box`1.Get", "'[App]M:App.Box`1.Get'" )]
    [InlineData( "qualifiedId eq '[App]M:App.Box`1.Get'", "\"qualifiedId eq '[App]M:App.Box\\`1.Get'\"" )]
    [InlineData( "name eq 'a\"$b\\'", "\"name eq 'a\\\"\\$b\\\\'\"" )]
    public void AWordIsQuotedSoThatItIsReadLiterally( string word, string quoted )
    {
        Assert.Equal( quoted, RelatedLists.Quote( word ) );
        Assert.Equal( [word], QueryLineSplitter.Split( quoted ) );
    }

    [Fact]
    public void AListSaysHowManyObjectsSatisfyTheConditions()
    {
        var page = this.RunJson( "list", "aspect-instances", "--limit", "2" );

        Assert.Equal( 5, page.GetProperty( "count" ).GetInt32() );
        Assert.True( page.GetProperty( "truncated" ).GetBoolean() );
        Assert.Equal( 2, page.GetProperty( "items" ).GetArrayLength() );
        Assert.Equal( "App", page.GetProperty( "byNamespace" )[0].GetProperty( "namespace" ).GetString() );

        var all = this.RunJson( "list", "aspect-instances", "--all" );
        Assert.False( all.GetProperty( "truncated" ).GetBoolean() );
        Assert.Equal( 5, all.GetProperty( "items" ).GetArrayLength() );

        Assert.Equal( 5, this.RunJson( "list", "aspect-instances", "--limit", "2", "--count" ).GetProperty( "count" ).GetInt32() );

        var (_, text, _) = this.Run( "list", "aspect-instances", "--limit", "2", "--format", "text" );
        Assert.Contains( "Showing 2 of 5 aspect-instances, from 1.", text, StringComparison.Ordinal );
    }

    [Fact]
    public void AListHasADefaultLimitButForJsonl()
    {
        Assert.Equal( ListCommandSettings.DefaultLimit, new ListCommandSettings().GetEffectiveLimit() );
        Assert.Null( new ListCommandSettings { All = true }.GetEffectiveLimit() );
        Assert.Null( new ListCommandSettings { Format = "jsonl" }.GetEffectiveLimit() );
        Assert.Equal( 7, new ListCommandSettings { Limit = 7, Format = "jsonl" }.GetEffectiveLimit() );
    }

    [Fact]
    public void ABatchWritesTheFilesOnce()
    {
        var (exitCode, output, error) = CommandRunner.Run(
            this._project.Directory,
            this._provider,
            ["batch", "--format", "json"],
            input: "list aspect-classes\nshow aspect-class Log\nschema aspect-classes" );

        Assert.True( exitCode == 0, error );

        var root = JsonDocument.Parse( output ).RootElement;
        Assert.Equal( "App", Assert.Single( root.GetProperty( "files" ).EnumerateArray() ).GetProperty( "project" ).GetString() );
        Assert.All( root.GetProperty( "results" ).EnumerateArray(), r => Assert.False( r.GetProperty( "result" ).TryGetProperty( "files", out _ ) ) );
        Assert.Equal( 3, root.GetProperty( "results" )[0].GetProperty( "result" ).GetProperty( "count" ).GetInt32() );
    }
}
