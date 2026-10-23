// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using System;
using Xunit;

namespace SharpCrafters.Aspects.Introspection.Tests;

public sealed class IntrospectionV1WriterTests
{
    private const string _header = """
                                   <?xml version="1.0" encoding="utf-8"?>
                                   <Symbols LinesOfCode="0" PostSharpLinesOfCode="0" EnhancedTypesCount="0" SchemaVersion="1" xmlns="http://schemas.postsharp.org/2.0/symbols">
                                   """;

    [Fact]
    public void TheRootElementHasItsAttributesInTheOrderOfPostSharp()
    {
        var xml = V1TestHelper.Write(
            _ => { },
            new SymbolsData { ProductVersion = "2027.0.6.0", LinesOfCode = 12, ProductLinesOfCode = 0, EnhancedTypesCount = 3 } );

        Assert.Equal(
            """
            <?xml version="1.0" encoding="utf-8"?>
            <Symbols LinesOfCode="12" PostSharpLinesOfCode="0" EnhancedTypesCount="3" SchemaVersion="1" PostSharpVersion="2027.0.6.0" xmlns="http://schemas.postsharp.org/2.0/symbols" />
            """.ToLf(),
            xml );
    }

    [Fact]
    public void AStringIsDefinedAtItsFirstUseAndReferencedAfter()
    {
        var xml = V1TestHelper.Write(
            writer =>
            {
                var a = writer.DefineDeclaration( "a", "M:A" );
                var b = writer.DefineDeclaration( "b", "M:B" );

                // A declaration and a string of the same text share a name.
                writer.WriteDeclarationMapping( new DeclarationMappingData { Source = a, Target = b } );
                writer.WriteDeclarationMapping( new DeclarationMappingData { Source = b, Target = a } );
                writer.WriteAnnotation( new AnnotationData { TargetDeclaration = a, Description = "M:B", Arguments = "x" } );
                writer.WriteAnnotation( new AnnotationData { TargetDeclaration = b, Description = "x", Arguments = " " } );
            } );

        Assert.Equal(
            _header + """

                        <Evolution Source="#1=M:A" Target="#2=M:B" />
                        <Evolution Source="#2" Target="#1" />
                        <Annotation Target="#1" Arguments="#3=x" Description="#2" />
                        <Annotation Target="#2" Description="#3" />
                      </Symbols>
                      """.ToLf(),
            xml );
    }

    [Fact]
    public void TwoKeysWithTheSameIdentifierHaveDifferentHandlesAndShareTheirName()
    {
        V1TestHelper.Write(
            writer =>
            {
                var first = writer.DefineDeclaration( "first", "M:A" );
                var second = writer.DefineDeclaration( "second", "M:A" );

                Assert.NotEqual( first, second );
                Assert.Equal( "M:A", writer.GetIdentifier( second ) );
                Assert.True( writer.TryGetDeclaration( "second", out var found ) );
                Assert.Equal( second, found );

                writer.AddDeclarationKey( "third", first );
                Assert.True( writer.TryGetDeclaration( "third", out found ) );
                Assert.Equal( first, found );
                Assert.False( writer.TryGetDeclaration( "fourth", out _ ) );
            } );
    }

    [Fact]
    public void TheImpliedValuesAreOmitted()
    {
        var xml = V1TestHelper.Write(
            writer =>
            {
                var aspect = writer.DefineDeclaration( "aspect", "T:Aspect" );
                var type = writer.DefineDeclaration( "type", "T:C" );
                var method = writer.DefineDeclaration( "method", "M:C::M()" );

                writer.WriteStartAspectClass( new AspectClassData { AspectClass = aspect } );
                writer.WriteStartPredecessor( new PredecessorData { Declaration = type, Id = "1" } );

                // The target is the declaration of the predecessor; the transformation changes the target with an advice of the
                // aspect class, at the implied order.
                writer.WriteStartAspectInstance( new AspectInstanceData { TargetDeclaration = type } );
                writer.WriteTransformation( new TransformationData { TargetDeclaration = type, AdviceDeclaration = aspect, Order = 1 } );
                writer.WriteTransformation( new TransformationData { Order = 0, LinesOfCodeAvoided = 0 } );
                writer.WriteEndAspectInstance();

                writer.WriteStartAspectInstance( new AspectInstanceData { TargetDeclaration = method } );
                writer.WriteTransformation( new TransformationData { TargetDeclaration = type, AdviceDeclaration = method, Order = 2 } );
                writer.WriteEndAspectInstance();

                writer.WriteEndPredecessor();
                writer.WriteEndAspectClass();
            } );

        Assert.Equal(
            _header + """

                        <Class Class="#1=T:Aspect">
                          <Instance Declaration="#2=T:C" Id="1">
                            <Target>
                              <JoinPoint />
                              <JoinPoint Ordinal="0" LinesAvoided="0" />
                            </Target>
                            <Target Target="#3=M:C::M()">
                              <JoinPoint Advised="#2" Advising="#3" Ordinal="2" />
                            </Target>
                          </Instance>
                        </Class>
                      </Symbols>
                      """.ToLf(),
            xml );
    }

    [Fact]
    public void AnImpliedValueIsOmittedByHandleNotByIdentifier()
    {
        // Two objects of the compiler with the same name, such as a declaration and its translation into the module, get two
        // handles, and the advice is then written although its name is that of the aspect class, as WriteWeavingSymbolsTask of
        // PostSharp writes it.
        var xml = V1TestHelper.Write(
            writer =>
            {
                var aspect = writer.DefineDeclaration( "aspect", "T:Aspect" );
                var translatedAspect = writer.DefineDeclaration( "translated aspect", writer.GetIdentifier( aspect ) );
                var type = writer.DefineDeclaration( "type", "T:C" );

                writer.WriteStartAspectClass( new AspectClassData { AspectClass = aspect } );
                writer.WriteStartPredecessor( new PredecessorData { Declaration = type } );
                writer.WriteStartAspectInstance( new AspectInstanceData { TargetDeclaration = type } );
                writer.WriteTransformation( new TransformationData { AdviceDeclaration = translatedAspect, Order = 1 } );
                writer.WriteEndAspectInstance();
                writer.WriteEndPredecessor();
                writer.WriteEndAspectClass();
            } );

        Assert.Contains( "<JoinPoint Advising=\"#1\" />", xml, StringComparison.Ordinal );
    }

    [Fact]
    public void TheLocationOfAPredecessorIsWrittenOnlyWithItsFile()
    {
        var xml = V1TestHelper.Write(
            writer =>
            {
                writer.WriteStartAspectClass( new AspectClassData { AspectClass = writer.DefineDeclaration( "aspect", "T:Aspect" ) } );
                writer.WriteStartPredecessor( new PredecessorData { Line = 1, Column = 2 } );
                writer.WriteEndPredecessor();
                writer.WriteStartPredecessor( new PredecessorData { File = "a.config", Column = 2 } );
                writer.WriteEndPredecessor();
                writer.WriteStartPredecessor( new PredecessorData { File = "a.config", Line = 1, Column = 2 } );
                writer.WriteEndPredecessor();
                writer.WriteEndAspectClass();
            } );

        Assert.Contains( "<Instance />", xml, StringComparison.Ordinal );
        Assert.Contains( "<Instance File=\"a.config\" />", xml, StringComparison.Ordinal );
        Assert.Contains( "<Instance File=\"a.config\" Line=\"1\" Column=\"2\" />", xml, StringComparison.Ordinal );
    }

    [Fact]
    public void TheSectionsComeInTheirOrder()
    {
        V1TestHelper.Write(
            writer =>
            {
                var a = writer.DefineDeclaration( "a", "M:A" );
                writer.WriteAnnotation( new AnnotationData { TargetDeclaration = a } );

                Assert.Throws<InvalidOperationException>( () => writer.WriteDeclarationMapping( new DeclarationMappingData { Source = a, Target = a } ) );
                Assert.Throws<InvalidOperationException>( () => writer.WriteStartAspectClass( new AspectClassData { AspectClass = a } ) );
            } );
    }

    [Fact]
    public void TheElementsAreNestedAsTheFormatRequires()
    {
        V1TestHelper.Write(
            writer =>
            {
                var a = writer.DefineDeclaration( "a", "M:A" );

                Assert.Throws<InvalidOperationException>( () => writer.WriteStartPredecessor( default ) );
                Assert.Throws<InvalidOperationException>( () => writer.WriteEndAspectClass() );

                writer.WriteStartAspectClass( new AspectClassData { AspectClass = a } );
                Assert.Throws<InvalidOperationException>( () => writer.WriteTransformation( new TransformationData { Order = 1 } ) );
                Assert.Throws<InvalidOperationException>( () => writer.WriteAnnotation( new AnnotationData { TargetDeclaration = a } ) );
                writer.WriteStartPredecessor( default );
                Assert.Throws<InvalidOperationException>( () => writer.WriteEndAspectClass() );

                // Dispose writes the end of the elements that are still open.
            } );
    }

    [Fact]
    public void ARequiredDeclarationIsValidated()
    {
        V1TestHelper.Write(
            writer =>
            {
                var a = writer.DefineDeclaration( "a", "M:A" );

                Assert.Throws<ArgumentException>( () => writer.WriteDeclarationMapping( new DeclarationMappingData { Source = a, Target = default } ) );
                Assert.Throws<ArgumentException>( () => writer.WriteDeclarationMapping( default ) );
                Assert.Throws<ArgumentException>( () => writer.WriteStartAspectClass( default ) );
                Assert.Throws<ArgumentException>( () => writer.DefineDeclaration( "b", " " ) );
                Assert.Throws<ArgumentException>( () => writer.DefineDeclaration( "a", "M:B" ) );

                // A handle of another writer.
                Assert.Throws<ArgumentException>( () => writer.WriteDeclarationMapping( new DeclarationMappingData { Source = a, Target = new DeclarationHandle( 99 ) } ) );
            } );
    }
}
