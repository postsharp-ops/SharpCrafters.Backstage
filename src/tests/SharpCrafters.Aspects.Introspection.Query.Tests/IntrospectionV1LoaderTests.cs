// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using System;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;

namespace SharpCrafters.Aspects.Introspection.Query.Tests;

public sealed class IntrospectionV1LoaderTests
{
    private static IntrospectionModel Load( string body, string? assembly = null, string? projectDirectory = null )
        => IntrospectionV1Loader.Load(
            new MemoryStream( Encoding.UTF8.GetBytes( $"""<Symbols xmlns="http://schemas.postsharp.org/2.0/symbols" PostSharpVersion="2027.0.6" LinesOfCode="12">{body}</Symbols>""" ) ),
            assembly,
            projectDirectory );

    [Fact]
    public void AnAttributeOnItsTargetIsAnAttribute()
    {
        var model = Load(
            """
            <Class Class="T:[App]App.LogAttribute">
              <Instance Declaration="M:[App]App.C::Run()" Id="1"><Target><JoinPoint Description="Wrapped" /></Target></Instance>
            </Class>
            """ );

        var instance = Assert.Single( model.AspectInstances );
        var predecessor = Assert.Single( instance.Predecessors );

        Assert.Equal( "App", model.Assembly );
        Assert.Equal( "2027.0.6", model.ProductVersion );
        Assert.Equal( 12, model.LinesOfCode );
        Assert.Equal( "App.LogAttribute", instance.AspectClass.FullName );
        Assert.Equal( "Log", instance.AspectClass.ShortName );
        Assert.Equal( "[App]M:App.C.Run", instance.TargetDeclaration.QualifiedId );
        Assert.Equal( AspectPredecessorKind.Attribute, predecessor.Kind );
        Assert.True( predecessor.IsKindInferred );
        Assert.Same( instance.AspectClass, predecessor.AttributeClass );
        Assert.Same( instance.TargetDeclaration, predecessor.Declaration );

        var transformation = Assert.Single( instance.Transformations );
        Assert.Same( instance.TargetDeclaration, transformation.TargetDeclaration );
        Assert.Same( instance.AspectClass.Declaration, transformation.AdviceDeclaration );
        Assert.Equal( 1, transformation.Order );
        Assert.Same( transformation, Assert.Single( model.Transformations ) );
    }

    [Fact]
    public void TheKindOfAPredecessorIsInferredWhenTheFileDoesNotGiveIt()
    {
        var model = Load(
            """
            <Class Class="T:[App]App.LogAttribute">
              <Instance Declaration="A:App" Id="1"><Target Target="M:[App]App.C::Run()" /><Target Target="M:[App]App.C::Stop()" /></Instance>
              <Instance Declaration="T:[App]App.C"><Target /></Instance>
              <Instance Declaration="T:[App]App.D"><Target Target="M:[App]App.D::Run()" /></Instance>
              <Instance Declaration="A:Lib" ForwardRef="true"><Target Target="M:[App]App.E::Run()" /></Instance>
              <Instance File="C:\src\App\postsharp.config" Line="5" Column="6"><Target Target="M:[App]App.F::Run()" /></Instance>
              <Instance Declaration="M:[App]App.G::Run()" Origin="Inherited"><Target /></Instance>
            </Class>
            """ );

        Assert.Equal(
            new[]
            {
                AspectPredecessorKind.Multicast, AspectPredecessorKind.Attribute, AspectPredecessorKind.Multicast, AspectPredecessorKind.Inherited,
                AspectPredecessorKind.Xml, AspectPredecessorKind.Inherited
            },
            model.Predecessors.Select( p => p.Kind ) );

        Assert.Equal( new[] { true, true, true, true, true, false }, model.Predecessors.Select( p => p.IsKindInferred ) );
        Assert.Equal( 2, model.Predecessors[0].AspectInstances.Count );

        var xml = model.Predecessors[4];
        Assert.Null( xml.Declaration );
        Assert.Null( xml.AttributeClass );
        Assert.Equal( @"C:\src\App\postsharp.config", xml.File );
        Assert.Equal( 5, xml.Line );
        Assert.Equal( 6, xml.Column );
    }

    [Fact]
    public void TheTransformationsOfAnAspectProviderBecomeChildAspectInstances()
    {
        // As AspectProvider.t.pssym of the PostSharp snapshot tests: the provided aspect instance that is not standalone is
        // written under the aspect provider, and its advice is its own aspect class.
        var model = Load(
            """
            <Class Class="T:[App]App.ProviderAttribute">
              <Instance Declaration="T:[App]App.Target" Id="1">
                <Target LinesAvoided="2">
                  <JoinPoint Advised="M:[App]App.Target::Merged()" Advising="T:[App]App.BoundaryAspect" Description="Wrapped" Ordinal="0" />
                  <JoinPoint Advised="M:[App]App.Target::Merged()" Advising="M:[App]App.BoundaryAspect::OnExit()" Description="Wrapped too" Ordinal="2" />
                  <JoinPoint Advised="M:[App]App.Target::Other()" Advising="T:[App]App.BoundaryAspect" Ordinal="3" />
                  <JoinPoint Advising="M:[App]App.ProviderAttribute::Initialize()" Ordinal="4" />
                </Target>
              </Instance>
            </Class>
            """ );

        Assert.Equal( 3, model.AspectInstances.Count );

        var provider = model.AspectInstances[0];
        Assert.Equal( "App.ProviderAttribute", provider.AspectClass.FullName );

        // An advice that is a member of the aspect class is of the aspect class.
        Assert.Equal( 4, Assert.Single( provider.Transformations ).Order );

        var merged = model.AspectInstances[1];
        Assert.Equal( "App.BoundaryAspect", merged.AspectClass.FullName );
        Assert.Equal( "[App]M:App.Target.Merged", merged.TargetDeclaration.QualifiedId );
        Assert.Equal( new[] { 0, 2 }, merged.Transformations.Select( t => t.Order ) );

        var child = Assert.Single( merged.Predecessors );
        Assert.Equal( AspectPredecessorKind.ChildAspect, child.Kind );
        Assert.True( child.IsKindInferred );
        Assert.Same( provider, child.AspectInstance );

        // The aspect instances of one aspect provider share one predecessor.
        Assert.Same( child, Assert.Single( model.AspectInstances[2].Predecessors ) );
        Assert.Equal( 2, model.Predecessors.Count );
        Assert.Equal( new[] { "App.ProviderAttribute", "App.BoundaryAspect" }, model.AspectClasses.Select( c => c.FullName ) );
        Assert.Equal( 4, model.Transformations.Count );
    }

    [Fact]
    public void AnAdviceNamedLikeTheAspectClassIsOfTheAspectClass()
    {
        // WriteWeavingSymbolsTask writes Advising when the advice is another object of the compiler than the aspect class.
        var model = Load(
            """
            <Class Class="T:[Lib]Lib.NotifyPropertyChangedAttribute">
              <Instance Declaration="T:[App]App.Person"><Target><JoinPoint Advising="T:[Lib]Lib.NotifyPropertyChangedAttribute" /></Target></Instance>
            </Class>
            """ );

        Assert.Single( Assert.Single( model.AspectInstances ).Transformations );
        Assert.Single( model.Predecessors );
    }

    [Fact]
    public void APredecessorProvidedByAnAspectInstanceRefersToIt()
    {
        // The changes of section 8: the provided aspect instance is its own Target, under an Instance of kind ChildAspect.
        var model = Load(
            """
            <Class Class="T:[App]App.ProviderAttribute">
              <Instance Declaration="T:[App]App.Target"><Target Id="7" /></Instance>
            </Class>
            <Class Class="T:[App]App.BoundaryAspect">
              <Instance Origin="ChildAspect" ProvidedBy="7"><Target Target="M:[App]App.Target::Merged()" /></Instance>
            </Class>
            """ );

        var predecessor = model.Predecessors[1];
        Assert.Equal( AspectPredecessorKind.ChildAspect, predecessor.Kind );
        Assert.False( predecessor.IsKindInferred );
        Assert.Same( model.AspectInstances[0], predecessor.AspectInstance );
        Assert.Equal( "aspect:" + model.AspectInstances[0].StructuralKey, predecessor.StructuralKey );
    }

    [Fact]
    public void TheDeclarationMappingsAreApplied()
    {
        // As Locations.t.pssym of the PostSharp snapshot tests: a field encapsulated as a property by a location aspect.
        var model = Load(
            """
            <Evolution Source="F:[App]App.C::Field" Target="F:[App]App.C::&lt;Field&gt;k__OriginalField" />
            <Evolution Source="F:[App]App.C::&lt;Field&gt;k__OriginalField" Target="P:[App]App.C::Field()" />
            <Class Class="T:[App]App.LocationAttribute">
              <Instance Declaration="F:[App]App.C::&lt;Field&gt;k__OriginalField">
                <Target><JoinPoint Semantic="Getter" /><JoinPoint Advised="P:[App]App.C::Field()" Semantic="Setter" /></Target>
              </Instance>
            </Class>
            <Annotation Target="F:[App]App.C::&lt;Field&gt;k__OriginalField" Description="Annotated" />
            """ );

        var instance = Assert.Single( model.AspectInstances );
        Assert.Equal( "[App]F:App.C.Field", instance.TargetDeclaration.QualifiedId );
        Assert.Equal( AspectPredecessorKind.Attribute, Assert.Single( instance.Predecessors ).Kind );
        Assert.All( instance.Transformations, t => Assert.Same( instance.TargetDeclaration, t.TargetDeclaration ) );
        Assert.Same( instance.TargetDeclaration, Assert.Single( model.Annotations ).TargetDeclaration );

        // The mappings themselves are kept as the file gives them.
        Assert.Equal(
            new[] { "[App]F:App.C.Field -> [App]F:App.C.<Field>k__OriginalField", "[App]F:App.C.<Field>k__OriginalField -> [App]P:App.C.Field" },
            model.DeclarationMappings.Select( m => $"{m.Source.QualifiedId} -> {m.Target.QualifiedId}" ) );
    }

    [Fact]
    public void TheMembersAreLinkedToTheirAccessorsAndBackingFields()
    {
        var model = Load(
            """
            <Class Class="T:[App]App.LogAttribute">
              <Instance Declaration="T:[App]App.C">
                <Target>
                  <JoinPoint Advised="P:[App]App.C::Name()" />
                  <JoinPoint Advised="M:[App]App.C::get_Name()" />
                  <JoinPoint Advised="M:[App]App.C::set_Name(System.String)" />
                  <JoinPoint Advised="F:[App]App.C::&lt;Name&gt;k__BackingField" />
                  <JoinPoint Advised="P:[App]App.C::Item(System.Int32)" />
                  <JoinPoint Advised="P:[App]App.C::Item(System.String)" />
                  <JoinPoint Advised="M:[App]App.C::get_Item(System.String)" />
                  <JoinPoint Advised="M:[App]App.C::set_Item(System.String, System.Int32)" />
                  <JoinPoint Advised="E:[App]App.C::Changed" />
                  <JoinPoint Advised="M:[App]App.C::add_Changed(System.EventHandler)" />
                  <JoinPoint Advised="M:[App]App.C::remove_Changed(System.EventHandler)" />
                  <JoinPoint Advised="R:[App]App.C::set_Name(System.String)@value" />
                </Target>
              </Instance>
            </Class>
            """ );

        var type = model.GetDeclaration( "[App]T:App.C" )!;
        var name = model.GetDeclaration( "[App]P:App.C.Name" )!;
        var stringIndexer = model.GetDeclaration( "[App]P:App.C.Item(System.String)" )!;
        var changed = model.GetDeclaration( "[App]E:App.C.Changed" )!;
        var value = model.GetDeclaration( "[App]M:App.C.set_Name(System.String);Parameter=value" )!;

        Assert.Equal( new[] { "get_Name", "set_Name" }, name.Accessors.Select( a => a.Name ) );
        Assert.Equal( "<Name>k__BackingField", name.BackingField!.Name );
        Assert.Same( name, name.BackingField.ContainingDeclaration );
        Assert.Equal( new[] { "get_Item", "set_Item" }, stringIndexer.Accessors.Select( a => a.Name ) );
        Assert.Empty( model.GetDeclaration( "[App]P:App.C.Item(System.Int32)" )!.Accessors );
        Assert.Equal( new[] { "add_Changed", "remove_Changed" }, changed.Accessors.Select( a => a.Name ) );
        Assert.Same( type, name.DeclaringType );
        Assert.Contains( name, type.Members );
        Assert.Equal( "App", name.Namespace );
        Assert.Equal( IntrospectionDeclarationKind.Parameter, value.Kind );
        Assert.Equal( "set_Name", value.ContainingDeclaration!.Name );
        Assert.Same( type, value.DeclaringType );
        Assert.DoesNotContain( value, type.Members );
    }

    [Fact]
    public void TheIdentifiersAreTheHashesOfTheStructuralKeys()
    {
        var model = Load(
            """
            <Class Class="T:[App]App.LogAttribute">
              <Instance Declaration="M:[App]App.C::Run()"><Target /></Instance>
              <Instance Declaration="M:[App]App.C::Run()"><Target /></Instance>
              <Instance Declaration="A:App"><Target Target="M:[App]App.C::Stop()" /></Instance>
            </Class>
            """ );

        // Two attributes of the same class on one declaration are numbered in the order of the file.
        Assert.Equal( "attribute:[App]M:App.C.Run:App.LogAttribute#1", model.Predecessors[0].StructuralKey );
        Assert.Equal( "attribute:[App]M:App.C.Run:App.LogAttribute#2", model.Predecessors[1].StructuralKey );
        Assert.Equal( "multicast:[App]A:App:App.LogAttribute", model.Predecessors[2].StructuralKey );
        Assert.Equal( "App.LogAttribute@[App]M:App.C.Run#1", model.AspectInstances[0].StructuralKey );
        Assert.Equal( "App.LogAttribute@[App]M:App.C.Run#2", model.AspectInstances[1].StructuralKey );
        Assert.Equal( "App.LogAttribute@[App]M:App.C.Stop", model.AspectInstances[2].StructuralKey );

        foreach ( var id in model.AspectInstances.Select( i => i.Id ).Concat( model.Predecessors.Select( p => p.Id ) ) )
        {
            Assert.Matches( "^[0-9a-f]{12}$", id );
        }

        Assert.Equal( model.AspectInstances.Count + model.Predecessors.Count, model.AspectInstances.Select( i => i.Id ).Concat( model.Predecessors.Select( p => p.Id ) ).Distinct().Count() );

        // The identifier is a function of the key alone, so it stays the same across builds and loads. The value pins the
        // format of the key and the hash: a change of either changes the identifiers that agents have kept.
        Assert.Equal( StructuralKeys.GetIdentifier( "App.LogAttribute@[App]M:App.C.Stop" ), model.AspectInstances[2].Id );
        Assert.Equal( "2c7584ca3e04", StructuralKeys.GetIdentifier( "App.LogAttribute@[App]M:App.C.Stop" ) );
    }

    [Fact]
    public void TheXmlPredecessorHasAKeyOfItsPosition()
    {
        var model = Load(
            """
            <Class Class="T:[App]App.LogAttribute">
              <Instance File="postsharp.config" Line="5" Column="6"><Target Target="M:[App]App.F::Run()" /></Instance>
            </Class>
            """ );

        Assert.Equal( "xml:postsharp.config(5,6)", Assert.Single( model.Predecessors ).StructuralKey );
    }

    [Fact]
    public void TheFileOfAnXmlPredecessorIsRelativeToTheProjectInItsKey()
    {
        // PostSharp writes the full path of the file, which differs between machines.
        var root = OperatingSystem.IsWindows() ? @"C:\src\App" : "/src/App";
        var file = Path.Combine( root, "Config", "postsharp.config" );

        var body = $"""
                    <Class Class="T:[App]App.LogAttribute">
                      <Instance File="{file}" Line="5" Column="6"><Target Target="M:[App]App.F::Run()" /></Instance>
                    </Class>
                    """;

        var predecessor = Assert.Single( Load( body, projectDirectory: root ).Predecessors );
        Assert.Equal( "xml:Config/postsharp.config(5,6)", predecessor.StructuralKey );
        Assert.Equal( file, predecessor.File );

        // Another machine, another directory: the same identifier.
        var otherRoot = OperatingSystem.IsWindows() ? @"D:\work\App" : "/work/App";
        var otherBody = body.Replace( root, otherRoot, StringComparison.Ordinal );
        Assert.Equal( predecessor.Id, Assert.Single( Load( otherBody, projectDirectory: otherRoot ).Predecessors ).Id );

        // A file outside of the project keeps its path.
        Assert.Equal(
            "xml:" + file.Replace( '\\', '/' ) + "(5,6)",
            Assert.Single( Load( body, projectDirectory: Path.Combine( root, "Other" ) ).Predecessors ).StructuralKey );
    }

    [Fact]
    public void TheAssemblyIsTheOneGivenOrThatOfMostTargets()
    {
        const string body = """
                            <Class Class="T:[Lib]Lib.LogAttribute">
                              <Instance Declaration="A:Lib" ForwardRef="true"><Target Target="M:[App]App.C::Run()" /><Target Target="M:[App]App.C::Stop()" /></Instance>
                            </Class>
                            """;

        Assert.Equal( "App", Load( body ).Assembly );
        Assert.Equal( "Other", Load( body, "Other" ).Assembly );
    }

    [Fact]
    public void AnnotationsAreLoaded()
    {
        var model = Load(
            """<Annotation Target="P:[App]App.Person::FullName()" Class="T:[Lib]Lib.DependsOn" Arguments="2" Description="The FullName property depends on FirstName." LinesAvoided="1" />""" );

        var annotation = Assert.Single( model.Annotations );
        Assert.Equal( "[App]P:App.Person.FullName", annotation.TargetDeclaration.QualifiedId );
        Assert.Equal( "[Lib]T:Lib.DependsOn", annotation.AnnotationClass!.QualifiedId );
        Assert.Equal( "2", annotation.Arguments );
        Assert.Equal( "The FullName property depends on FirstName.", annotation.Description );
        Assert.Equal( 1, annotation.LinesOfCodeAvoided );
    }

    [Fact]
    public void EveryFixtureIsLoaded()
    {
        foreach ( var file in Directory.GetFiles( Path.Combine( AppContext.BaseDirectory, "Fixtures" ), "*.pssym", SearchOption.AllDirectories ) )
        {
            using var stream = File.OpenRead( file );
            var model = IntrospectionV1Loader.Load( stream );

            Assert.All( model.AspectInstances, i => Assert.NotEmpty( i.Predecessors ) );
            Assert.All( model.Declarations, d => Assert.DoesNotContain( "::", d.Id, StringComparison.Ordinal ) );
            Assert.All( model.Declarations, d => Assert.DoesNotContain( "[", d.Id.Replace( "[]", "", StringComparison.Ordinal ).Replace( "[0:", "", StringComparison.Ordinal ), StringComparison.Ordinal ) );
        }
    }

    [Fact]
    public void AFileOfPostSharpIsLoaded()
    {
        using var stream = File.OpenRead( Path.Combine( AppContext.BaseDirectory, "Fixtures", "PostSharp.Patterns.Aggregation.Tests.pssym" ) );
        var model = IntrospectionV1Loader.Load( stream );

        Assert.Equal( "PostSharp.Patterns.Aggregation.Tests", model.Assembly );
        Assert.NotEmpty( model.DeclarationMappings );
        Assert.Contains( model.AspectClasses, c => c.ShortName == "Aggregatable" );

        // A file of PostSharp maps the backing field of a property that an aspect intercepts: no aspect instance or
        // transformation targets the original field that PostSharp introduced.
        Assert.DoesNotContain( model.Transformations, t => t.TargetDeclaration.Name.EndsWith( "k__OriginalField", StringComparison.Ordinal ) );
        Assert.Equal( model.AspectInstances.Count, model.AspectInstances.Select( i => i.Id ).Distinct().Count() );
    }
}
