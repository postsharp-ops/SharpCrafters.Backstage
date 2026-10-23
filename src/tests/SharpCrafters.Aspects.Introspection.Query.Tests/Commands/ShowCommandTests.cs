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
/// <c>introspect show</c> and <c>introspect schema</c>.
/// </summary>
public sealed class ShowCommandTests : IDisposable
{
    private readonly TestProject _project = new();
    private readonly TestProvider _provider;

    public ShowCommandTests()
    {
        this._provider = new TestProvider { HasLocations = true };
        this._provider.Locations["P:App.Customer.FullName"] = new IntrospectionSourceLocation( this._project.SourceFile, 18, 19 );
        this._provider.Locations["T:App.Customer"] = new IntrospectionSourceLocation( this._project.SourceFile, 7, 5 );
    }

    public void Dispose() => this._project.Dispose();

    private (int ExitCode, string Output, string Error) Run( params string[] arguments ) => CommandRunner.Run( this._project.Directory, this._provider, arguments );

    private JsonElement RunJson( params string[] arguments )
    {
        var (exitCode, output, error) = this.Run( arguments.Concat( ["--format", "json"] ).ToArray() );
        Assert.True( exitCode == 0, error );

        return JsonDocument.Parse( output ).RootElement;
    }

    [Fact]
    public void ADeclarationIsShownWithWhatAppliesToItsAccessors()
    {
        // The property FullName: its getter is logged and raises notifications, and the property has an annotation.
        var result = this.RunJson( "show", "declaration", "Customer.FullName" );

        Assert.Equal( "P:App.Customer.FullName", result.GetProperty( "declaration" ).GetProperty( "id" ).GetString() );
        Assert.Equal( "Customer.cs:18:19", result.GetProperty( "declaration" ).GetProperty( "location" ).GetString() );
        Assert.Equal( "M:App.Customer.get_FullName", result.GetProperty( "relatedDeclarations" )[0].GetProperty( "id" ).GetString() );

        var instances = result.GetProperty( "aspectInstances" ).EnumerateArray().ToList();
        Assert.Equal( ["Log", "NotifyPropertyChanged"], instances.Select( i => i.GetProperty( "aspectClass" ).GetProperty( "shortName" ).GetString() ).Order() );

        // Show expands the predecessors and the transformations of the aspect instances.
        var notify = instances.Single( i => i.GetProperty( "aspectClass" ).GetProperty( "shortName" ).GetString() == "NotifyPropertyChanged" );
        Assert.Equal( "Attribute", notify.GetProperty( "predecessors" )[0].GetProperty( "kind" ).GetString() );
        Assert.Equal( "Getter", notify.GetProperty( "transformations" )[0].GetProperty( "semantic" ).GetString() );

        Assert.Equal( "The FullName property depends on FirstName.", result.GetProperty( "annotations" )[0].GetProperty( "description" ).GetString() );
    }

    [Fact]
    public void ADeclarationIsSelectedByItsPosition()
    {
        var (exitCode, output, error) = this.Run( "show", "declaration", "Customer.cs:18" );

        Assert.True( exitCode == 0, error );
        Assert.StartsWith( "Property App.Customer.FullName at Customer.cs:18:19 (App).", output, StringComparison.Ordinal );
        Assert.Contains( "Aspect NotifyPropertyChanged [", output, StringComparison.Ordinal );
        Assert.Contains( "3. App.Customer.get_FullName (Getter): Activates notifications for the property", output, StringComparison.Ordinal );
        Assert.Contains( "Created by the attribute [", output, StringComparison.Ordinal );
        Assert.Contains( "The FullName property depends on FirstName.", output, StringComparison.Ordinal );
    }

    [Fact]
    public void ADeclarationIsSelectedByItsPositionInAnAbsolutePath()
    {
        // On Windows, the path starts with a drive letter and a colon, like a documentation comment identifier.
        var (exitCode, output, error) = this.Run( "show", "declaration", Path.Combine( this._project.Directory, "Customer.cs" ) + ":18" );

        Assert.True( exitCode == 0, error );
        Assert.StartsWith( "Property App.Customer.FullName at Customer.cs:18:19 (App).", output, StringComparison.Ordinal );
    }

    [Fact]
    public void APositionWithoutAspectsSaysSo()
    {
        this._provider.Locations["T:App.Unrelated"] = new IntrospectionSourceLocation( this._project.SourceFile, 30, 1 );

        var (exitCode, output, _) = this.Run( "show", "declaration", "Customer.cs:30" );

        Assert.Equal( 0, exitCode );
        Assert.Contains( "No aspect applies to T:App.Unrelated", output, StringComparison.Ordinal );
    }

    [Fact]
    public void AnExactIdentifierSelectsOneOverload()
        => Assert.Equal(
            "M:App.Repository.Get(System.Int32)",
            this.RunJson( "show", "declaration", "M:App.Repository.Get(System.Int32)" ).GetProperty( "declaration" ).GetProperty( "id" ).GetString() );

    [Fact]
    public void AnAmbiguousNameListsTheCandidates()
    {
        // A name that one declaration ends with selects it; one that two overloads end with is ambiguous.
        Assert.Equal( 0, this.Run( "show", "declaration", "App.Customer" ).ExitCode );

        File.WriteAllText(
            Path.Combine( this._project.Directory, "bin", "Debug", "net10.0", "App.pssym" ),
            TestProject.DefaultSymbols.Replace(
                "<Annotation",
                """<Annotation Target="M:[App]App.Repository::Get(System.String)" Description="Overload" /><Annotation""",
                StringComparison.Ordinal ) );

        var (exitCode, _, error) = this.Run( "show", "declaration", "Repository.Get" );

        Assert.Equal( IntrospectionExitCodes.Ambiguous, exitCode );
        Assert.Contains( "'M:App.Repository.Get(System.Int32)'", error, StringComparison.Ordinal );
        Assert.Contains( "'M:App.Repository.Get(System.String)'", error, StringComparison.Ordinal );
    }

    [Fact]
    public void AnAspectInstanceIsSelectedByAPrefixOfItsIdentifier()
    {
        var id = this.RunJson( "list", "aspect-instances", "--where", "aspectClass/shortName eq 'Cache'", "--select", "id" ).GetProperty( "items" )[0].GetProperty( "id" ).GetString()!;

        var result = this.RunJson( "show", "aspect-instance", id.Substring( 0, 8 ) );

        Assert.Equal( id, result.GetProperty( "aspectInstance" ).GetProperty( "id" ).GetString() );
        Assert.Equal( "premium", result.GetProperty( "aspectInstance" ).GetProperty( "tags" )[0].GetString() );
        Assert.Single( result.GetProperty( "aspectInstance" ).GetProperty( "transformations" ).EnumerateArray() );
    }

    [Fact]
    public void APredecessorIsShownWithItsAspectInstances()
    {
        var id = this.RunJson( "list", "predecessors", "--where", "kind eq 'Multicast'", "--select", "id" ).GetProperty( "items" )[0].GetProperty( "id" ).GetString()!;

        var (exitCode, output, error) = this.Run( "show", "predecessor", id );

        Assert.True( exitCode == 0, error );
        Assert.StartsWith( $"Created by the multicast attribute [{id}] on App (kind inferred).", output, StringComparison.Ordinal );
        Assert.Contains( "Aspect Log [", output, StringComparison.Ordinal );
        Assert.Contains( "on method App.Repository.Save(App.Customer)", output, StringComparison.Ordinal );
    }

    [Fact]
    public void AnAspectClassIsSelectedByItsShortName()
    {
        var result = this.RunJson( "show", "aspect-class", "Log" );

        Assert.Equal( "App.LogAttribute", result.GetProperty( "aspectClass" ).GetProperty( "name" ).GetString() );
        Assert.Equal( 3, result.GetProperty( "aspectClass" ).GetProperty( "aspectInstances" ).GetArrayLength() );
    }

    [Theory]
    [InlineData( "transformation", "x", IntrospectionExitCodes.InvalidQuery, "whose objects have no identifier" )]
    [InlineData( "declarations", "x", IntrospectionExitCodes.InvalidQuery, "write 'declaration'" )]
    [InlineData( "aspect-instance", "zzz", IntrospectionExitCodes.InvalidQuery, "neither the identifier" )]
    [InlineData( "aspect-class", "Nope", IntrospectionExitCodes.InvalidQuery, "The aspect classes are: NotifyPropertyChanged, Log, Cache." )]
    [InlineData( "declaration", "Nothing", IntrospectionExitCodes.InvalidQuery, "list declarations" )]
    [InlineData( "diagnostic", "x", IntrospectionExitCodes.InvalidQuery, "whose objects have no identifier" )]
    public void AnInvalidSelectorIsAnError( string collection, string selector, int expectedExitCode, string message )
    {
        var (exitCode, _, error) = this.Run( "show", collection, selector );

        Assert.Equal( expectedExitCode, exitCode );
        Assert.Contains( message, error, StringComparison.Ordinal );
    }

    [Fact]
    public void ThePositionNeedsSourceLocations()
    {
        var (exitCode, _, error) = CommandRunner.Run( this._project.Directory, new TestProvider(), ["show", "declaration", "Customer.cs:18"] );

        Assert.Equal( IntrospectionExitCodes.InvalidQuery, exitCode );
        Assert.Contains( "records no source location", error, StringComparison.Ordinal );
    }

    [Fact]
    public void TheSchemaDescribesTheFieldsAndWhatTheProductProvides()
    {
        var result = this.RunJson( "schema" );
        var collections = result.GetProperty( "collections" ).EnumerateArray().ToList();

        Assert.False( collections.Single( c => c.GetProperty( "name" ).GetString() == "diagnostics" ).GetProperty( "provided" ).GetBoolean() );

        var instances = collections.Single( c => c.GetProperty( "name" ).GetString() == "aspect-instances" );
        var fields = instances.GetProperty( "fields" ).EnumerateArray().ToDictionary( f => f.GetProperty( "name" ).GetString()! );
        Assert.Equal( "aspectClass", fields["aspectClass"].GetProperty( "type" ).GetString() );
        Assert.Equal( "transformation[]", fields["transformations"].GetProperty( "type" ).GetString() );
        Assert.False( fields["isSkipped"].GetProperty( "provided" ).GetBoolean() );

        var (exitCode, output, _) = this.Run( "schema", "predecessors" );
        Assert.Equal( 0, exitCode );
        Assert.Contains( "kind (enum): How the predecessor created its aspect instances: Attribute, a custom attribute", output, StringComparison.Ordinal );
        Assert.Contains( "ChildAspect, an aspect provider (IAspectProvider)", output, StringComparison.Ordinal );
        Assert.Contains( "Values: Attribute, Multicast, Inherited, Xml,", output, StringComparison.Ordinal );
    }
}
