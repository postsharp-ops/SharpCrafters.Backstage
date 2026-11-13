// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Aspects.Introspection.Query.Projects;
using SharpCrafters.Aspects.Introspection.Query.V2;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;

namespace SharpCrafters.Aspects.Introspection.Query.Tests.V2;

/// <summary>
/// The loading of a file of schema version 2, the <c>.mlsym</c> file of Metalama, into the common model.
/// </summary>
public sealed class IntrospectionV2LoaderTests
{
    public static string SampleFile => Path.Combine( AppContext.BaseDirectory, "Fixtures", "Metalama", "Sample.mlsym" );

    private static IntrospectionModel LoadSample()
    {
        using var stream = File.OpenRead( SampleFile );

        return IntrospectionV2Loader.Load( stream );
    }

    internal static IntrospectionModel Load( string aspectClasses, string? assembly = null )
        => IntrospectionV2Loader.Load(
            new MemoryStream( Encoding.UTF8.GetBytes( $$"""{ "schemaVersion": 2, "product": "Metalama", "assembly": "App", "aspectClasses": [ {{aspectClasses}} ] }""" ) ),
            assembly );

    [Fact]
    public void TheHeaderGivesTheAssemblyAndTheVersion()
    {
        var model = LoadSample();

        Assert.Equal( "Sample", model.Assembly );
        Assert.Equal( 2, model.SchemaVersion );
        Assert.Equal( "2027.0.8", model.ProductVersion );
    }

    [Fact]
    public void TheAssemblyOfTheCallerWins()
    {
        using var stream = File.OpenRead( SampleFile );

        Assert.Equal( "Other", IntrospectionV2Loader.Load( stream, "Other" ).Assembly );
    }

    [Fact]
    public void TheAspectClassesHaveTheirNamesDescriptionsAndLayers()
    {
        var model = LoadSample();

        var log = Assert.Single( model.AspectClasses, c => c.FullName == "Sample.LogAttribute" );
        Assert.Equal( "Log", log.ShortName );
        Assert.Equal( "Log", log.DisplayName );
        Assert.Equal( "Writes a message to the console when a method starts.", log.Description );
        Assert.Equal( [new IntrospectionAspectLayer( null, 0 ), new IntrospectionAspectLayer( "Build", 1 )], log.Layers );
        Assert.Equal( 2, log.AspectInstances.Count );

        // An aspect class of another assembly is qualified with it.
        var notify = Assert.Single( model.AspectClasses, c => c.FullName == "Sample.Aspects.NotifyPropertyChangedAttribute" );
        Assert.Equal( "[Sample.Aspects]T:Sample.Aspects.NotifyPropertyChangedAttribute", notify.Declaration.QualifiedId );
    }

    [Fact]
    public void TheAspectInstancesHaveTheirTransformationsAndDiagnostics()
    {
        var model = LoadSample();

        Assert.Equal( 4, model.AspectInstances.Count );

        var add = model.AspectInstances[0];
        Assert.Equal( "[Sample]M:Sample.Calculator.Add(System.Int32,System.Int32)", add.TargetDeclaration.QualifiedId );
        Assert.False( add.IsSkipped );
        Assert.Equal( new IntrospectionSourceLocation( "Calculator.cs", 8, 5 ), add.TargetDeclaration.Location );

        Assert.Collection(
            add.Transformations,
            t =>
            {
                Assert.Equal( "OverrideMember", t.Kind );
                Assert.Equal( "OverrideMethod", t.AdviceKind );
                Assert.Null( t.Layer );
                Assert.Equal( 1, t.Order );
                Assert.Null( t.IntroducedDeclaration );
            },
            t =>
            {
                Assert.Equal( "IntroduceMember", t.Kind );
                Assert.Equal( "Build", t.Layer );
                Assert.Equal( "[Sample]T:Sample.Calculator", t.TargetDeclaration.QualifiedId );
                Assert.Equal( "[Sample]F:Sample.Calculator._logger", t.IntroducedDeclaration?.QualifiedId );
            } );

        var divide = model.AspectInstances[1];
        Assert.True( divide.IsSkipped );
        var diagnostic = Assert.Single( divide.Diagnostics );
        Assert.Equal( "LOG001", diagnostic.Id );
        Assert.Equal( "Warning", diagnostic.Severity );
        Assert.Equal( "The method 'Divide' cannot be logged.", diagnostic.Message );
        Assert.Same( divide.TargetDeclaration, diagnostic.Declaration );
        Assert.Equal( "Calculator.cs", diagnostic.File );
        Assert.Equal( 12, diagnostic.Line );
        Assert.Null( diagnostic.Column );
        Assert.Same( diagnostic, Assert.Single( model.Diagnostics ) );
        Assert.Equal( 4, model.Transformations.Count );
    }

    [Fact]
    public void ThePredecessorsHaveTheirKindsAndReferences()
    {
        var model = LoadSample();

        var attribute = Assert.Single( model.AspectInstances[0].Predecessors );
        Assert.Equal( AspectPredecessorKind.Attribute, attribute.Kind );
        Assert.False( attribute.IsKindInferred );
        Assert.Same( model.AspectInstances[0].TargetDeclaration, attribute.Declaration );
        Assert.Equal( "Sample.LogAttribute", attribute.AttributeClass?.FullName );
        Assert.Equal( "Calculator.cs", attribute.File );
        Assert.Equal( 7, attribute.Line );
        Assert.Equal( 6, attribute.Column );

        var fabric = Assert.Single( model.AspectInstances[1].Predecessors );
        Assert.Equal( AspectPredecessorKind.Fabric, fabric.Kind );
        Assert.Equal( "[Sample]T:Sample.Fabric", fabric.Declaration?.QualifiedId );

        var inherited = Assert.Single( model.AspectInstances[2].Predecessors );
        Assert.Equal( AspectPredecessorKind.Inherited, inherited.Kind );
        Assert.Equal( "[Sample.Aspects]T:Sample.Aspects.EntityBase", inherited.Declaration?.QualifiedId );

        // A child aspect refers to its parent by the identifier of the file.
        var child = Assert.Single( model.AspectInstances[3].Predecessors );
        Assert.Equal( AspectPredecessorKind.ChildAspect, child.Kind );
        Assert.Equal( 1, child.Degree );
        Assert.Same( model.AspectInstances[2], child.AspectInstance );
        Assert.StartsWith( "aspect:", child.StructuralKey, StringComparison.Ordinal );
    }

    [Fact]
    public void EqualPredecessorsBecomeOne()
    {
        var model = Load(
            """
            { "id": "T:App.LogAttribute", "aspectInstances": [
                { "id": 1, "targetDeclaration": "M:App.C.M1", "predecessors": [ { "kind": "Fabric", "declaration": "T:App.Fabric" } ] },
                { "id": 2, "targetDeclaration": "M:App.C.M2", "predecessors": [ { "kind": "Fabric", "declaration": "T:App.Fabric" } ] } ] }
            """ );

        var fabric = Assert.Single( model.Predecessors );
        Assert.Equal( model.AspectInstances, fabric.AspectInstances );
        Assert.All( model.AspectInstances, i => Assert.Same( fabric, Assert.Single( i.Predecessors ) ) );
    }

    [Fact]
    public void AReferenceToALaterAspectInstanceIsResolved()
    {
        var model = Load(
            """
            { "id": "T:App.A", "aspectInstances": [ { "id": 1, "targetDeclaration": "T:App.C", "predecessors": [ { "kind": "RequiredAspect", "degree": 1, "aspectInstance": 2 } ] } ] },
            { "id": "T:App.B", "aspectInstances": [ { "id": 2, "targetDeclaration": "T:App.C" } ] }
            """ );

        var required = Assert.Single( model.AspectInstances[0].Predecessors );
        Assert.Equal( AspectPredecessorKind.RequiredAspect, required.Kind );
        Assert.Same( model.AspectInstances[1], required.AspectInstance );
    }

    [Fact]
    public void TheDeclarationsAreParsed()
    {
        var model = Load(
            """
            { "id": "T:App.LogAttribute", "aspectInstances": [
                { "id": 1, "targetDeclaration": "M:App.Box`1.Get``1(System.Collections.Generic.List{``0},System.Int32)" },
                { "id": 2, "targetDeclaration": "M:App.Box`1.Get``1(System.Collections.Generic.List{``0},System.Int32);Parameter=1" },
                { "id": 3, "targetDeclaration": "M:App.Box`1.#ctor;Return" },
                { "id": 4, "targetDeclaration": "P:App.Box`1.Value", "transformations": [ { "kind": "OverrideMember", "targetDeclaration": "M:App.Box`1.get_Value" } ] },
                { "id": 5, "targetDeclaration": "Assembly:Lib, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null" },
                { "id": 6, "targetDeclaration": "[Lib]E:Lib.Base.Changed" },
                { "id": 7, "targetDeclaration": "P:App.Box`1.Item(System.Int32)", "transformations": [ { "kind": "OverrideMember", "targetDeclaration": "M:App.Box`1.get_Item(System.Int32)~`0" } ] } ] }
            """ );

        var get = model.GetDeclaration( "[App]M:App.Box`1.Get``1(System.Collections.Generic.List{``0},System.Int32)" )!;
        Assert.Equal( IntrospectionDeclarationKind.Method, get.Kind );
        Assert.Equal( "Get", get.Name );
        Assert.Equal( "App", get.Namespace );

        var box = get.DeclaringType!;
        Assert.Equal( "[App]T:App.Box`1", box.QualifiedId );
        Assert.Equal( "Box", box.Name );
        Assert.Contains( get, box.Members );

        var parameter = model.AspectInstances[1].TargetDeclaration;
        Assert.Equal( IntrospectionDeclarationKind.Parameter, parameter.Kind );
        Assert.Same( get, parameter.ContainingDeclaration );
        Assert.DoesNotContain( parameter, box.Members );

        Assert.Equal( IntrospectionDeclarationKind.ReturnParameter, model.AspectInstances[2].TargetDeclaration.Kind );
        Assert.Equal( IntrospectionDeclarationKind.Constructor, model.AspectInstances[2].TargetDeclaration.ContainingDeclaration?.Kind );

        // An accessor is linked to its property.
        var property = model.AspectInstances[3].TargetDeclaration;
        Assert.Equal( "get_Value", Assert.Single( property.Accessors ).Name );

        // An identifier of Metalama ends with the return type of the method, after ~.
        var indexer = model.AspectInstances[6].TargetDeclaration;
        Assert.Equal( "get_Item", Assert.Single( indexer.Accessors ).Name );

        var assembly = model.AspectInstances[4].TargetDeclaration;
        Assert.Equal( (IntrospectionDeclarationKind.Assembly, "Lib", "A:Lib"), (assembly.Kind, assembly.Assembly, assembly.Id) );

        var changed = model.AspectInstances[5].TargetDeclaration;
        Assert.Equal( (IntrospectionDeclarationKind.Event, "Lib", "[Lib]T:Lib.Base"), (changed.Kind, changed.Assembly, changed.DeclaringType?.QualifiedId) );
    }

    [Fact]
    public void AMemberWithoutParametersEndsBeforeItsReturnType()
    {
        var model = Load(
            """
            { "id": "T:App.LogAttribute", "aspectInstances": [
                { "id": 1, "targetDeclaration": "P:App.C.Value", "transformations": [ { "kind": "OverrideMember", "targetDeclaration": "M:App.C.get_Value~System.Int32" } ] } ] }
            """ );

        var getter = model.GetDeclaration( "[App]M:App.C.get_Value~System.Int32" )!;
        Assert.Equal( "get_Value", getter.Name );
        Assert.Equal( "[App]T:App.C", getter.DeclaringType?.QualifiedId );
        Assert.Same( getter, Assert.Single( model.AspectInstances[0].TargetDeclaration.Accessors ) );
        Assert.Null( model.GetDeclaration( "[App]T:App.C.get_Value~System" ) );
    }

    [Fact]
    public void TheAttributesOfTwoAspectsOnOneDeclarationAreTwoPredecessors()
    {
        var model = Load(
            """
            { "id": "T:App.LogAttribute", "aspectInstances": [ { "id": 1, "targetDeclaration": "M:App.C.M", "predecessors": [ { "kind": "Attribute", "declaration": "M:App.C.M" } ] } ] },
            { "id": "T:App.CacheAttribute", "aspectInstances": [ { "id": 2, "targetDeclaration": "M:App.C.M", "predecessors": [ { "kind": "Attribute", "declaration": "M:App.C.M" } ] } ] }
            """ );

        Assert.Equal( 2, model.Predecessors.Count );
        Assert.Equal( "App.LogAttribute", Assert.Single( model.AspectInstances[0].Predecessors ).AttributeClass?.FullName );
        Assert.Equal( "App.CacheAttribute", Assert.Single( model.AspectInstances[1].Predecessors ).AttributeClass?.FullName );
    }

    [Fact]
    public void AnInvalidIdentifierThrows()
        => Assert.Throws<FormatException>( () => Load( """{ "id": "X:App.A" }""" ) );

    [Fact]
    public void TheIdentifiersAreStableAcrossLoads()
    {
        var first = LoadSample();
        var second = LoadSample();

        Assert.Equal( first.AspectInstances.Select( i => i.Id ), second.AspectInstances.Select( i => i.Id ) );
        Assert.Equal( first.Predecessors.Select( p => p.Id ), second.Predecessors.Select( p => p.Id ) );
        Assert.Equal( first.AspectInstances.Count, first.AspectInstances.Select( i => i.Id ).Distinct().Count() );
    }

    [Fact]
    public void TheIndexKeepsTheFieldsOfVersion2()
    {
        var model = LoadSample();
        var written = ModelIndexTests.Write( model );
        var read = ModelIndex.Read( new BinaryReader( new MemoryStream( written ), Encoding.UTF8 ) );

        Assert.Equal( written, ModelIndexTests.Write( read ) );
        Assert.Equal( Describe( model ), Describe( read ) );
    }

    [Fact]
    public void TheMergeKeepsTheFieldsOfVersion2AndOneCopyOfEachDiagnostic()
    {
        var (merged, _) = ModelMerger.Merge( [(CreateTarget( "net10.0" ), LoadSample()), (CreateTarget( "net48" ), LoadSample())] );

        Assert.Equal( Describe( LoadSample() ), Describe( merged ) );
    }

    [Fact]
    public void TheMergeKeepsTheTransformationsOfTwoLayersAndTheFrameworksOfADiagnostic()
    {
        const string aspectClasses = """
                                     { "id": "T:App.LogAttribute", "aspectInstances": [ { "id": 1, "targetDeclaration": "M:App.C.M",
                                         "transformations": [
                                             { "kind": "OverrideMember", "targetDeclaration": "M:App.C.M" },
                                             { "kind": "OverrideMember", "layer": "Second", "targetDeclaration": "M:App.C.M" } ],
                                         "diagnostics": [
                                             { "id": "X1", "severity": "Warning", "message": "M", "declaration": "M:App.C.M" },
                                             { "id": "X1", "severity": "Warning", "message": "M", "declaration": "T:App.C" } ] } ] }
                                     """;

        var net10 = CreateTarget( "net10.0" );
        var net48 = CreateTarget( "net48" );
        var (merged, provenance) = ModelMerger.Merge( [(net10, Load( aspectClasses )), (net48, Load( aspectClasses ))] );

        Assert.Equal( [null, "Second"], merged.Transformations.Select( t => t.Layer ) );
        Assert.Equal( 2, merged.Diagnostics.Count );
        Assert.All( merged.Diagnostics, d => Assert.Equal( [net10, net48], provenance.GetTargets( d ) ) );
        Assert.All( merged.Transformations, t => Assert.Equal( [net10, net48], provenance.GetTargets( t ) ) );
    }

    private static IntrospectionTarget CreateTarget( string framework )
    {
        var directory = Path.Combine( Path.GetTempPath(), "introspection-v2" );

        return new IntrospectionTarget(
            new IntrospectionProject( Path.Combine( directory, "Sample.csproj" ), true ),
            framework,
            "Debug",
            "Sample",
            Path.Combine( directory, "bin", "Sample.mlsym" ),
            new Dictionary<string, string>(),
            null );
    }

    // The fields of version 2, which ModelIndexTests does not describe.
    private static List<string> Describe( IntrospectionModel model )
        => model.AspectClasses.Select( c => $"{c.FullName} {c.DisplayName} {c.Description} {string.Join( ",", c.Layers )}" )
            .Concat(
                model.AspectInstances.Select(
                    i => $"{i.Id} {i.IsSkipped} {i.TargetDeclaration.Location} "
                         + string.Join( ";", i.Predecessors.Select( p => $"{p.StructuralKey} {p.Degree} {p.File}({p.Line},{p.Column})" ) ) + " "
                         + string.Join( ";", i.Transformations.Select( t => $"{t.Kind} {t.AdviceKind} {t.Layer} {t.Order} {t.IntroducedDeclaration?.QualifiedId}" ) ) + " "
                         + string.Join( ";", i.Diagnostics.Select( d => $"{d.Id} {d.Severity} {d.Message} {d.Declaration?.QualifiedId} {d.File}({d.Line},{d.Column})" ) ) ) )
            .Concat( model.Diagnostics.Select( d => d.ToString() ) )
            .ToList();
}
