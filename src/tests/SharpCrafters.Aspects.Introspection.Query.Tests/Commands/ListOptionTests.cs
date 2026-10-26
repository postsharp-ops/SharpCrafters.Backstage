// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using System;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace SharpCrafters.Aspects.Introspection.Query.Tests.Commands;

/// <summary>
/// The orderings, the comparisons, the selections and the outputs of <c>introspect list</c>.
/// </summary>
public sealed class ListOptionTests : IDisposable
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
    public void SeveralOrderingsApplyInTurnAndANullComesFirst()
    {
        // Four transformations have no semantic: they come first, by descending order, then the getter and the setter.
        var items = this.RunJson( "list", "transformations", "--order-by", "semantic asc,order desc", "--select", "order" )
            .GetProperty( "items" )
            .EnumerateArray()
            .Select( i => i.GetProperty( "order" ).GetInt32() );

        Assert.Equal( [6, 5, 2, 1, 3, 4], items );
    }

    [Theory]
    [InlineData( "order sub 1 eq 2", 1 )]
    [InlineData( "order mul 2 eq 8", 1 )]
    [InlineData( "order div 2 eq 1", 2 )]
    [InlineData( "order mod 2 eq 0", 3 )]
    [InlineData( "order div 0 eq null", 6 )]
    [InlineData( "order mod 0 eq null", 6 )]

    // In parentheses: Spectre.Console reads an argument that starts with '-' as an option.
    [InlineData( "(-order) lt 0", 6 )]
    [InlineData( "(-order) eq -3", 1 )]
    public void TheArithmeticOperatorsAreEvaluated( string condition, int count )
        => Assert.Equal( count, this.RunJson( "list", "transformations", "--where", condition ).GetProperty( "count" ).GetInt32() );

    [Theory]
    [InlineData( "substring(aspectClass/shortName, 1) eq 'og'", 3 )]
    [InlineData( "substring(aspectClass/shortName, 0, 2) eq 'Ca'", 1 )]
    [InlineData( "'premium' in tags", 1 )]
    public void TheFunctionsAndTheMembershipOfACollectionAreEvaluated( string condition, int count )
        => Assert.Equal( count, this.RunJson( "list", "aspect-instances", "--where", condition ).GetProperty( "count" ).GetInt32() );

    [Theory]
    [InlineData( "order mul 9223372036854775807 gt 0", "Multiply" )]
    [InlineData( "order add 9223372036854775807 gt 0", "Add" )]
    [InlineData( "order sub 9223372036854775807 sub 9 lt 0", "Subtract" )]
    public void AnIntegerOverflowIsAnError( string condition, string operation )
    {
        var (exitCode, _, error) = this.Run( "list", "transformations", "--where", condition );

        Assert.Equal( IntrospectionExitCodes.InvalidQuery, exitCode );
        Assert.Contains( $"--where: The result of the operator '{operation}' is beyond the range of a 64-bit integer.", error, StringComparison.Ordinal );
    }

    [Fact]
    public void TheExpressionIsWrittenOnceInAnError()
    {
        const string condition = "id eq 'a";

        var (_, _, error) = this.Run( "list", "aspect-instances", "--where", condition );

        Assert.Contains( "There is an unterminated string literal at position 8.", error, StringComparison.Ordinal );
        Assert.Single( error.Split( condition ).Skip( 1 ) );
    }

    [Fact]
    public void AnOrderingByAnObjectIsAnError()
    {
        var (exitCode, _, error) = this.Run( "list", "aspect-instances", "--order-by", "aspectClass" );

        Assert.Equal( IntrospectionExitCodes.InvalidQuery, exitCode );
        Assert.Contains( "--order-by: The $orderby expression must evaluate to a single value of primitive type", error, StringComparison.Ordinal );
    }

    [Theory]
    [InlineData( "order lt 3", 2 )]
    [InlineData( "order le 3", 3 )]
    [InlineData( "order ge 5", 2 )]
    [InlineData( "order gt 6", 0 )]
    public void TheComparisonsAreEvaluated( string condition, int count )
        => Assert.Equal( count, this.RunJson( "list", "transformations", "--where", condition ).GetProperty( "count" ).GetInt32() );

    // Microsoft.OData.Core reads an integer beyond Int64 as a decimal, which is compared with the value of the field.
    [Theory]
    [InlineData( "linesOfCodeAvoided eq 9223372036854775808", 0 )]
    [InlineData( "linesOfCodeAvoided lt 9223372036854775808", 5 )]
    public void AnIntegerBeyondInt64IsComparedAsADecimal( string condition, int count )
        => Assert.Equal( count, this.RunJson( "list", "aspect-instances", "--where", condition ).GetProperty( "count" ).GetInt32() );

    [Theory]
    [InlineData( "aspectClass,aspectClass/hasLimitedLicense", "'aspectClass' and 'aspectClass/hasLimitedLicense' overlap" )]
    [InlineData( "aspectClass/shortName,aspectClass", "'aspectClass' and 'aspectClass/shortName' overlap" )]
    [InlineData( "id,id", "'id' is selected twice." )]
    public void AnOverlapOfTheSelectionIsAnError( string selection, string message )
    {
        var (exitCode, _, error) = this.Run( "list", "aspect-instances", "--select", selection );

        Assert.Equal( IntrospectionExitCodes.InvalidQuery, exitCode );
        Assert.Contains( "--select: " + message, error, StringComparison.Ordinal );
    }

    [Fact]
    public void TwoFieldsOfAnObjectAreSelected()
    {
        var aspectClass = this.RunJson( "list", "aspect-instances", "--select", "aspectClass/shortName,aspectClass/hasLimitedLicense" )
            .GetProperty( "items" )[0]
            .GetProperty( "aspectClass" );

        Assert.True( aspectClass.TryGetProperty( "shortName", out _ ) );
        Assert.True( aspectClass.TryGetProperty( "hasLimitedLicense", out _ ) );
    }

    [Fact]
    public void ADefaultColumnThatTheProductDoesNotRecordIsLeftOut()
    {
        string Header() => this.Run( "list", "transformations" ).Output.Split( '\n' )[0];

        Assert.Contains( "semantic", Header(), StringComparison.Ordinal );

        this._provider.Unprovided.Add( "transformation.semantic" );

        var (exitCode, _, error) = this.Run( "list", "transformations" );
        Assert.True( exitCode == 0, error );
        Assert.DoesNotContain( "semantic", Header(), StringComparison.Ordinal );
        Assert.Contains( "targetDeclaration", Header(), StringComparison.Ordinal );
    }

    [Fact]
    public void TheCountIsWrittenInJsonWithoutTheObjects()
    {
        var result = this.RunJson( "list", "aspect-instances", "--count" );

        Assert.Equal( 5, result.GetProperty( "count" ).GetInt32() );
        Assert.False( result.TryGetProperty( "items", out _ ) );
        Assert.Equal( 1, result.GetProperty( "files" ).GetArrayLength() );
    }

    [Fact]
    public void TheTextTableHasTheSelectedColumns()
    {
        var (exitCode, output, error) = this.Run( "list", "aspect-instances", "--select", "aspectClass/shortName,transformations/$count", "--order-by", "aspectClass/shortName" );

        Assert.True( exitCode == 0, error );

        var lines = output.Split( '\n' ).Select( l => l.TrimEnd( '\r' ) ).ToList();
        Assert.StartsWith( "aspectClass/shortName  transformations/$count", lines[0], StringComparison.Ordinal );
        Assert.StartsWith( "Cache", lines[2], StringComparison.Ordinal );
    }
}
