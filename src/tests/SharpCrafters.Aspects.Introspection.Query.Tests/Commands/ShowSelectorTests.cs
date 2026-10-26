// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using System;
using System.Linq;
using System.Text;
using System.Text.Json;
using Xunit;

namespace SharpCrafters.Aspects.Introspection.Query.Tests.Commands;

/// <summary>
/// The selectors of <c>introspect show</c>, and the related objects that it writes.
/// </summary>
public sealed class ShowSelectorTests : IDisposable
{
    private readonly TestProject _project = new();
    private readonly TestProvider _provider = new() { HasLocations = true };

    public void Dispose() => this._project.Dispose();

    private (int ExitCode, string Output, string Error) Run( params string[] arguments ) => CommandRunner.Run( this._project.Directory, this._provider, arguments );

    private JsonElement RunJson( params string[] arguments )
    {
        var (exitCode, output, error) = this.Run( [.. arguments, "--format", "json"] );
        Assert.True( exitCode == 0, error );

        return JsonDocument.Parse( output ).RootElement;
    }

    private void WriteSymbols( string symbols ) => this._project.WriteSymbols( "net10.0", symbols );

    [Fact]
    public void TheSchemaOffersShowOnlyForTheKindsThatShowTakes()
    {
        var (exitCode, text, error) = this.Run( "schema" );

        Assert.True( exitCode == 0, error );
        Assert.Contains( "aspect-instances (show aspect-instance):", text, StringComparison.Ordinal );
        Assert.Contains( "transformations:", text, StringComparison.Ordinal );
        Assert.DoesNotContain( "(show transformation)", text, StringComparison.Ordinal );

        var showable = this.RunJson( "schema" )
            .GetProperty( "collections" )
            .EnumerateArray()
            .ToDictionary( c => c.GetProperty( "name" ).GetString()!, c => c.GetProperty( "showable" ).GetBoolean() );

        Assert.Equal( ["aspect-classes", "aspect-instances", "declarations", "predecessors"], showable.Where( c => c.Value ).Select( c => c.Key ).Order( StringComparer.Ordinal ) );

        (exitCode, _, error) = this.Run( "show", "transformation", "x" );
        Assert.Equal( IntrospectionExitCodes.InvalidQuery, exitCode );
        Assert.Contains( "show is not available for transformation", error, StringComparison.Ordinal );
    }

    [Fact]
    public void AnAspectInstanceIsSelectedByItsStructuralKey()
    {
        const string key = "App.CacheAttribute@[App]M:App.Repository.Get(System.Int32)";

        Assert.Equal( key, this.RunJson( "show", "aspect-instance", key ).GetProperty( "aspectInstance" ).GetProperty( "key" ).GetString() );
    }

    [Fact]
    public void APrefixThatNoIdentifierStartsWithIsAnError()
    {
        var ids = this.RunJson( "list", "aspect-instances", "--select", "id" ).GetProperty( "items" ).EnumerateArray().Select( i => i.GetProperty( "id" ).GetString()! ).ToList();
        var prefix = "0123456789abcdef".First( c => ids.All( id => id[0] != c ) ).ToString();

        var (exitCode, _, error) = this.Run( "show", "aspect-instance", prefix );

        Assert.Equal( IntrospectionExitCodes.InvalidQuery, exitCode );
        Assert.Contains( $"No aspect instance has an identifier that starts with '{prefix}'.", error, StringComparison.Ordinal );
    }

    [Fact]
    public void APrefixOfSeveralIdentifiersIsAmbiguous()
    {
        // Seventeen aspect instances: two of them have identifiers that start with the same hexadecimal digit.
        var instances = new StringBuilder();

        for ( var i = 0; i < 17; i++ )
        {
            instances.Append( $"""<Instance Declaration="M:[App]App.C::M{i}()"><Target><JoinPoint /></Target></Instance>""" );
        }

        this.WriteSymbols( $"""<Symbols xmlns="{TestProject.SymbolsNamespace}"><Class Class="T:[App]App.LogAttribute">{instances}</Class></Symbols>""" );

        var ids = this.RunJson( "list", "aspect-instances", "--select", "id" ).GetProperty( "items" ).EnumerateArray().Select( i => i.GetProperty( "id" ).GetString()! ).ToList();
        var shared = ids.GroupBy( id => id[0] ).First( g => g.Count() > 1 );

        var (exitCode, _, error) = this.Run( "show", "aspect-instance", shared.Key.ToString() );

        Assert.Equal( IntrospectionExitCodes.Ambiguous, exitCode );
        Assert.Contains( $"'{shared.Key}' is the prefix of the identifiers of several aspect instances:", error, StringComparison.Ordinal );
        Assert.All( shared, id => Assert.Contains( id, error, StringComparison.Ordinal ) );
    }

    [Fact]
    public void AShortNameOfSeveralAspectClassesIsAmbiguous()
    {
        this.WriteSymbols(
            TestProject.DefaultSymbols.Replace(
                "<Annotation",
                """<Class Class="T:[Lib]Lib.LogAttribute"><Instance Declaration="M:[App]App.Repository::Save([App]App.Customer)"><Target><JoinPoint /></Target></Instance></Class><Annotation""",
                StringComparison.Ordinal ) );

        var (exitCode, _, error) = this.Run( "show", "aspect-class", "Log" );

        Assert.Equal( IntrospectionExitCodes.Ambiguous, exitCode );
        Assert.Contains( "App.LogAttribute", error, StringComparison.Ordinal );
        Assert.Contains( "Lib.LogAttribute", error, StringComparison.Ordinal );

        // The full name, and the short name in another case when it is unique.
        Assert.Equal( "Lib.LogAttribute", this.RunJson( "show", "aspect-class", "Lib.LogAttribute" ).GetProperty( "aspectClass" ).GetProperty( "name" ).GetString() );
        Assert.Equal( "App.CacheAttribute", this.RunJson( "show", "aspect-class", "cache" ).GetProperty( "aspectClass" ).GetProperty( "name" ).GetString() );
    }

    [Fact]
    public void APositionInAMemberWithoutAspectSelectsTheTypeThatContainsIt()
    {
        // The declarations of the line, the innermost first, as a source location database gives them: a method that the file does
        // not mention, then its type, which it does.
        this._provider.Locations["M:App.Customer.Unrelated"] = new IntrospectionSourceLocation( this._project.SourceFile, 9, 9 );
        this._provider.Locations["T:App.Customer"] = new IntrospectionSourceLocation( this._project.SourceFile, 9, 5 );

        var (exitCode, output, error) = this.Run( "show", "declaration", "Customer.cs:9" );

        Assert.True( exitCode == 0, error );
        Assert.StartsWith( "Type App.Customer at Customer.cs:9:5 (App).", output, StringComparison.Ordinal );
    }

    [Fact]
    public void APositionSelectsTheDeclarationOfTheAssemblyOfTheProject()
    {
        // A declaration of another assembly with the same identifier comes first in the file.
        this.WriteSymbols(
            TestProject.DefaultSymbols.Replace(
                "<Evolution",
                """<Class Class="T:[Lib]Lib.TraceAttribute"><Instance Declaration="T:[Lib]App.Customer"><Target><JoinPoint /></Target></Instance></Class><Evolution""",
                StringComparison.Ordinal ) );

        this._provider.Locations["T:App.Customer"] = new IntrospectionSourceLocation( this._project.SourceFile, 7, 5 );

        Assert.Equal( "App", this.RunJson( "show", "declaration", "Customer.cs:7" ).GetProperty( "declaration" ).GetProperty( "assembly" ).GetString() );
    }

    [Fact]
    public void AnAbsoluteWindowsPathIsAPosition()
    {
        this._provider.Locations["P:App.Customer.FullName"] = new IntrospectionSourceLocation( this._project.SourceFile, 18, 19 );

        // On Windows, the path starts with a drive letter and a colon, as a documentation comment identifier does.
        var (exitCode, output, error) = this.Run( "show", "declaration", this._project.SourceFile + ":18" );

        Assert.True( exitCode == 0, error );
        Assert.StartsWith( "Property App.Customer.FullName at Customer.cs:18:19 (App).", output, StringComparison.Ordinal );
    }

    [Theory]
    [InlineData( "Customer.cs:2147483648" )]
    [InlineData( "Customer.cs:0" )]
    public void ALineOutOfRangeIsAnInvalidQuery( string selector )
    {
        var (exitCode, _, error) = this.Run( "show", "declaration", selector );

        Assert.Equal( IntrospectionExitCodes.InvalidQuery, exitCode );
        Assert.Contains( "is not a line number", error, StringComparison.Ordinal );
    }

    [Fact]
    public void APositionWithoutAspectIsValidJson()
    {
        this._provider.Locations["T:App.Unrelated"] = new IntrospectionSourceLocation( this._project.SourceFile, 30, 1 );

        var result = this.RunJson( "show", "declaration", "Customer.cs:30" );

        Assert.Equal( JsonValueKind.Null, result.GetProperty( "declaration" ).ValueKind );
        Assert.Contains( "No aspect applies to T:App.Unrelated", result.GetProperty( "message" ).GetString(), StringComparison.Ordinal );
        Assert.Equal( 1, result.GetProperty( "files" ).GetArrayLength() );

        var (_, jsonl, _) = this.Run( "show", "declaration", "Customer.cs:30", "--format", "jsonl" );
        Assert.Equal( JsonValueKind.Null, JsonDocument.Parse( jsonl ).RootElement.GetProperty( "declaration" ).ValueKind );
    }

    [Theory]
    [InlineData( "aspect-instance", "is neither the identifier of an aspect instance" )]
    [InlineData( "predecessor", "is neither the identifier of a predecessor" )]
    public void AnInvalidIdentifierNamesWhatItShouldIdentify( string collection, string message )
    {
        var (exitCode, _, error) = this.Run( "show", collection, "zzz" );

        Assert.Equal( IntrospectionExitCodes.InvalidQuery, exitCode );
        Assert.Contains( message, error, StringComparison.Ordinal );
    }

    [Fact]
    public void APropertyIsShownWithItsAccessorsItsBackingFieldAndItsDeclarationMappings()
    {
        // FirstName has a setter that raises notifications, and a backing field that the weaver renamed.
        var symbols = TestProject.DefaultSymbols.Replace(
            "<Annotation",
            """<Class Class="T:[App]App.TraceAttribute"><Instance Declaration="F:[App]App.Customer::&lt;FirstName&gt;k__BackingField"><Target><JoinPoint /></Target></Instance></Class><Annotation""",
            StringComparison.Ordinal );

        this.WriteSymbols( symbols );

        var result = this.RunJson( "show", "declaration", "Customer.set_FirstName" );

        Assert.Equal( "M:App.Customer.set_FirstName(System.String)", result.GetProperty( "declaration" ).GetProperty( "id" ).GetString() );

        var field = this.RunJson( "show", "declaration", "F:App.Customer.<FirstName>k__BackingField" );
        var mapping = Assert.Single( field.GetProperty( "declarationMappings" ).EnumerateArray() );
        Assert.Equal( "F:App.Customer.<FirstName>k__OriginalField", mapping.GetProperty( "target" ).GetProperty( "id" ).GetString() );

        var (exitCode, text, error) = this.Run( "show", "declaration", "F:App.Customer.<FirstName>k__BackingField" );
        Assert.True( exitCode == 0, error );
        Assert.Contains( "Declaration mappings:", text, StringComparison.Ordinal );
        Assert.Contains( "<FirstName>k__OriginalField", text, StringComparison.Ordinal );
    }

    [Fact]
    public void TheMembersOfATypeAreIncludedWithMembers()
    {
        var type = this.RunJson( "show", "declaration", "App.Customer" );
        var withMembers = this.RunJson( "show", "declaration", "App.Customer", "--members" );

        var related = withMembers.GetProperty( "relatedDeclarations" ).EnumerateArray().Select( d => d.GetProperty( "id" ).GetString() ).ToList();
        Assert.Contains( "P:App.Customer.FullName", related );
        Assert.Contains( "M:App.Customer.get_FullName", related );

        // The aspect instances of the members are added to that of the type, each once.
        var instances = withMembers.GetProperty( "aspectInstances" ).EnumerateArray().Select( i => i.GetProperty( "id" ).GetString() ).ToList();
        Assert.True( instances.Count > type.GetProperty( "aspectInstances" ).GetArrayLength() );
        Assert.Equal( instances.Count, instances.Distinct().Count() );
    }

    [Fact]
    public void EachKindOfPredecessorIsDescribed()
    {
        this.WriteSymbols(
            $"""
             <Symbols xmlns="{TestProject.SymbolsNamespace}">
               <Class Class="T:[App]App.LogAttribute">
                 <Instance File="aspects.xml" Line="3" Column="5"><Target Target="M:[App]App.Xml::Run()"><JoinPoint /></Target></Instance>
                 <Instance Declaration="T:[App]App.Base" Origin="Inherited"><Target Target="M:[App]App.Derived::Run()"><JoinPoint /></Target></Instance>
               </Class>
               <Class Class="T:[App]App.ProviderAttribute">
                 <Instance Declaration="T:[App]App.Target"><Target Id="7" /></Instance>
               </Class>
               <Class Class="T:[App]App.BoundaryAspect">
                 <Instance Origin="ChildAspect" ProvidedBy="7"><Target Target="M:[App]App.Target::Merged()"><JoinPoint /></Target></Instance>
               </Class>
             </Symbols>
             """ );

        var predecessors = this.RunJson( "list", "predecessors", "--select", "id,kind,location" ).GetProperty( "items" ).EnumerateArray().ToList();
        var xml = predecessors.Single( p => p.GetProperty( "kind" ).GetString() == "Xml" );
        Assert.Equal( "aspects.xml:3:5", xml.GetProperty( "location" ).GetString() );

        string Describe( string kind )
        {
            var id = predecessors.Single( p => p.GetProperty( "kind" ).GetString() == kind ).GetProperty( "id" ).GetString()!;
            var (exitCode, output, error) = this.Run( "show", "predecessor", id );
            Assert.True( exitCode == 0, error );

            return output;
        }

        Assert.StartsWith( "Created by the XML element [", Describe( "Xml" ), StringComparison.Ordinal );
        Assert.Contains( "] at aspects.xml:3:5", Describe( "Xml" ), StringComparison.Ordinal );
        Assert.StartsWith( "Created by the inherited attribute [", Describe( "Inherited" ), StringComparison.Ordinal );
        Assert.Contains( "on App.Base", Describe( "Inherited" ), StringComparison.Ordinal );
        Assert.StartsWith( "Provided by the aspect Provider [", Describe( "ChildAspect" ), StringComparison.Ordinal );
    }
}
