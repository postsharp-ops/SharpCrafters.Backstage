// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using System;
using System.IO;
using Xunit;

namespace SharpCrafters.Aspects.Introspection.Tests;

public sealed class IntrospectionV1ReaderTests
{
    private const string _namespace = "http://schemas.postsharp.org/2.0/symbols";

    [Fact]
    public void TheImpliedValuesAreReturned()
    {
        using var reader = V1TestHelper.CreateReader(
            $"""
             <Symbols xmlns="{_namespace}">
               <Class Class="#1=T:Aspect">
                 <Instance Declaration="#2=T:C">
                   <Target>
                     <JoinPoint />
                   </Target>
                 </Instance>
               </Class>
             </Symbols>
             """ );

        // A file without SchemaVersion, such as the fixtures of the Visual Studio extension, is of version 1.
        Assert.Equal( 1, reader.SchemaVersion );
        Assert.Null( reader.Symbols.ProductVersion );

        Assert.True( reader.Read() );
        var aspectClass = reader.AspectClass.AspectClass;
        Assert.True( reader.Read() );
        var predecessor = reader.Predecessor;
        Assert.Null( predecessor.Id );
        Assert.Null( predecessor.Kind );
        Assert.True( reader.Read() );
        Assert.Equal( predecessor.Declaration, reader.AspectInstance.TargetDeclaration );
        Assert.Equal( 0, reader.AspectInstance.LinesOfCodeAvoided );
        Assert.True( reader.Read() );
        Assert.Equal( predecessor.Declaration, reader.Transformation.TargetDeclaration );
        Assert.Equal( aspectClass, reader.Transformation.AdviceDeclaration );
        Assert.Equal( 1, reader.Transformation.Order );
        Assert.Null( reader.Transformation.LinesOfCodeAvoided );
    }

    [Fact]
    public void TheSameIdentifierGivesTheSameHandle()
    {
        using var reader = V1TestHelper.CreateReader(
            $"""<Symbols xmlns="{_namespace}"><Evolution Source="#1=M:A" Target="#2=M:B" /><Evolution Source="M:A" Target="#2" /></Symbols>""" );

        Assert.True( reader.Read() );
        var first = reader.DeclarationMapping;
        Assert.True( reader.Read() );
        var second = reader.DeclarationMapping;

        Assert.Equal( first.Source, second.Source );
        Assert.Equal( first.Target, second.Target );
        Assert.Same( reader.GetIdentifier( first.Target ), reader.GetIdentifier( second.Target ) );
    }

    [Fact]
    public void EmptyAndNonEmptyContainersGiveTheirEnd()
    {
        using var reader = V1TestHelper.CreateReader(
            $"""
             <Symbols xmlns="{_namespace}">
               <Class Class="#1=T:A" />
               <Class Class="#2=T:B"><Instance Declaration="#3=T:C"><Target /></Instance></Class>
             </Symbols>
             """ );

        Assert.Equal(
            new[]
            {
                IntrospectionNodeKind.AspectClass, IntrospectionNodeKind.EndAspectClass, IntrospectionNodeKind.AspectClass, IntrospectionNodeKind.Predecessor,
                IntrospectionNodeKind.AspectInstance, IntrospectionNodeKind.EndAspectInstance, IntrospectionNodeKind.EndPredecessor,
                IntrospectionNodeKind.EndAspectClass
            },
            ReadAll( reader ) );
    }

    [Fact]
    public void UnknownElementsAndAttributesAreIgnored()
    {
        using var reader = V1TestHelper.CreateReader(
            $"""
             <Symbols xmlns="{_namespace}" xmlns:x="urn:other" Future="1">
               <Future><Class Class="#1=T:Hidden" /></Future>
               <Evolution Source="#2=M:A" Target="#3=M:B" Kind="Teleported" Future="1"><Child /></Evolution>
               <x:Class Class="#4=T:Other" />
               <Class Class="#5=T:A">
                 <Instance Declaration="#6=T:C" Origin="Future">
                   <Unknown />
                   <Target><JoinPoint Advised="#2"><Child /></JoinPoint></Target>
                 </Instance>
               </Class>
             </Symbols>
             """ );

        Assert.Equal(
            new[]
            {
                IntrospectionNodeKind.DeclarationMapping, IntrospectionNodeKind.AspectClass, IntrospectionNodeKind.Predecessor, IntrospectionNodeKind.AspectInstance,
                IntrospectionNodeKind.Transformation, IntrospectionNodeKind.EndAspectInstance, IntrospectionNodeKind.EndPredecessor,
                IntrospectionNodeKind.EndAspectClass
            },
            ReadAll( reader ) );
    }

    [Fact]
    public void AnUnknownEnumeratedValueIsIgnored()
    {
        using var reader = V1TestHelper.CreateReader( $"""<Symbols xmlns="{_namespace}"><Evolution Source="#1=M:A" Target="#1" Kind="Teleported" /></Symbols>""" );

        Assert.True( reader.Read() );
        Assert.Null( reader.DeclarationMapping.Kind );
    }

    [Theory]
    [InlineData( """<Evolution Source="#1" Target="#2=M:B" />""", "#1" )]
    [InlineData( """<Evolution Source="#x=M:A" Target="#2=M:B" />""", "#x=M:A" )]
    [InlineData( """<Evolution Target="#2=M:B" />""", "'Source'" )]
    [InlineData( """<Class Class="#1=T:A"><Instance><Target /></Instance></Class>""", "'Target'" )]
    [InlineData( """<Class Class="#1=T:A"><Instance Declaration="#2=T:C"><Target LinesAvoided="many" /></Instance></Class>""", "many" )]
    [InlineData( """<Class Class="#1=T:A" LimitedLicense="maybe" />""", "maybe" )]
    [InlineData( """<Evolution Source="#1=M:A" Target="#1" """, "" )]
    public void AMalformedFileThrowsWithItsLine( string body, string expectedInMessage )
    {
        var exception = Assert.Throws<IntrospectionFormatException>(
            () =>
            {
                using var reader = V1TestHelper.CreateReader( $"<Symbols xmlns=\"{_namespace}\">\n{body}\n</Symbols>" );

                while ( reader.Read() ) { }
            } );

        Assert.Contains( expectedInMessage, exception.Message, StringComparison.Ordinal );

        // The body is on line 2. XML that is not well formed is detected where the parser meets the next token.
        Assert.InRange( exception.LineNumber, 2, 3 );
    }

    [Fact]
    public void TheRootElementMustBeThatOfTheFormat()
    {
        Assert.Throws<IntrospectionFormatException>( () => V1TestHelper.CreateReader( "<Symbols />" ) );
        Assert.Throws<IntrospectionFormatException>( () => V1TestHelper.CreateReader( $"<Other xmlns=\"{_namespace}\" />" ) );
    }

    [Fact]
    public void ADocumentTypeDefinitionIsProhibited()
    {
        Assert.Throws<IntrospectionFormatException>(
            () => V1TestHelper.CreateReader( $"<!DOCTYPE Symbols [<!ENTITY x \"y\">]><Symbols xmlns=\"{_namespace}\">&x;</Symbols>" ) );
    }

    [Fact]
    public void ADataStructureIsAvailableOnlyOnItsNode()
    {
        using var reader = V1TestHelper.CreateReader( $"""<Symbols xmlns="{_namespace}"><Evolution Source="#1=M:A" Target="#1" /></Symbols>""" );

        Assert.Throws<InvalidOperationException>( () => reader.DeclarationMapping );
        Assert.True( reader.Read() );
        Assert.Throws<InvalidOperationException>( () => reader.AspectClass );
        Assert.Throws<ArgumentException>( () => reader.GetIdentifier( default ) );
    }

    [Fact]
    public void AnExplicitImpliedValueIsReadAsItsEffectiveValue()
    {
        // WriteWeavingSymbolsTask writes Advising when the advice is another object of the compiler than the aspect class, even
        // when both have the same name.
        using var reader = V1TestHelper.CreateReader(
            $"""<Symbols xmlns="{_namespace}"><Class Class="#1=T:A"><Instance Declaration="#2=T:C"><Target Target="#2"><JoinPoint Advised="#2" Advising="#1" /></Target></Instance></Class></Symbols>""" );

        Assert.True( reader.Read() );
        var aspectClass = reader.AspectClass.AspectClass;
        Assert.True( reader.Read() );
        var declaration = reader.Predecessor.Declaration;
        Assert.True( reader.Read() );
        Assert.Equal( declaration, reader.AspectInstance.TargetDeclaration );
        Assert.True( reader.Read() );
        Assert.Equal( declaration, reader.Transformation.TargetDeclaration );
        Assert.Equal( aspectClass, reader.Transformation.AdviceDeclaration );
    }

    [Fact]
    public void AFileWithoutContentIsRead()
    {
        using var stream = File.OpenRead( Path.Combine( V1TestHelper.FixturesDirectory, "VisualStudio", "CSharp", "When_CodeWithoutAspect.pssym" ) );
        using var reader = IntrospectionV1Reader.Create( stream );

        Assert.Empty( ReadAll( reader ) );
        Assert.False( reader.Read() );
    }

    [Fact]
    public void AReaderRejectsTheHandlesOfAnotherReader()
    {
        var xml = $"""<Symbols xmlns="{_namespace}"><Evolution Source="#1=M:A" Target="#2=M:B" /></Symbols>""";
        using var first = V1TestHelper.CreateReader( xml );
        using var second = V1TestHelper.CreateReader( xml );
        Assert.True( first.Read() );
        Assert.True( second.Read() );

        // The same index in both readers.
        var handle = first.DeclarationMapping.Source;
        Assert.NotEqual( handle, second.DeclarationMapping.Source );
        Assert.Equal( "M:A", first.GetIdentifier( handle ) );
        Assert.Throws<ArgumentException>( () => second.GetIdentifier( handle ) );
    }

    [Theory]
    [InlineData( "<Symbols xmlns=\"{0}\" /><Extra />" )]
    [InlineData( "<Symbols xmlns=\"{0}\"><Evolution Source=\"M:A\" Target=\"M:B\" /></Symbols><Extra />" )]
    [InlineData( "<Symbols xmlns=\"{0}\"><Evolution Source=\"M:A\" Target=\"M:B\" /></Symbols>garbage" )]
    public void ContentAfterTheRootIsAnError( string format )
    {
        using var reader = V1TestHelper.CreateReader( string.Format( format, _namespace ) );

        Assert.Throws<IntrospectionFormatException>( () => ReadAll( reader ) );
    }

    [Fact]
    public void AnXmlReaderOfTheCallerIsLeftAtTheEndOfTheRoot()
    {
        // A fragment, whose second element the caller reads after the reader.
        var settings = new System.Xml.XmlReaderSettings { ConformanceLevel = System.Xml.ConformanceLevel.Fragment };
        using var xmlReader = System.Xml.XmlReader.Create( new StringReader( $"""<Symbols xmlns="{_namespace}" /><Next />""" ), settings );
        using var reader = IntrospectionV1Reader.Create( xmlReader );

        Assert.Empty( ReadAll( reader ) );
        Assert.True( xmlReader.Read() );
        Assert.Equal( "Next", xmlReader.LocalName );
    }

    [Theory]
    [InlineData( "Source=\"\" Target=\"M:B\"" )]
    [InlineData( "Source=\" \" Target=\"M:B\"" )]
    [InlineData( "Source=\"#1=\" Target=\"M:B\"" )]
    [InlineData( "Source=\"#1= \" Target=\"M:B\"" )]
    public void ABlankRequiredDeclarationIsAnError( string attributes )
    {
        using var reader = V1TestHelper.CreateReader( $"""<Symbols xmlns="{_namespace}"><Evolution {attributes} /></Symbols>""" );

        Assert.Throws<IntrospectionFormatException>( () => reader.Read() );
    }

    [Fact]
    public void TheOrderOfTheAttributesDoesNotMatter()
    {
        using var reader = V1TestHelper.CreateReader(
            $"""<Symbols xmlns="{_namespace}"><Evolution Target="#1=M:A" Source="#1" /><Annotation Description="#3" Target="#2=T:C" Arguments="#3=x" /></Symbols>""" );

        Assert.True( reader.Read() );
        Assert.Equal( "M:A", reader.GetIdentifier( reader.DeclarationMapping.Source ) );
        Assert.Equal( reader.DeclarationMapping.Source, reader.DeclarationMapping.Target );
        Assert.True( reader.Read() );
        Assert.Equal( "x", reader.Annotation.Description );
    }

    [Fact]
    public void TheNamesOfSkippedContentAreKept()
    {
        using var reader = V1TestHelper.CreateReader(
            $"""<Symbols xmlns="{_namespace}"><Future><Class Class="#1=T:A" /></Future><Other xmlns="urn:other" Name="#2=T:B" /><Annotation Target="#1" Class="#2" /></Symbols>""" );

        Assert.True( reader.Read() );
        Assert.Equal( "T:A", reader.GetIdentifier( reader.Annotation.TargetDeclaration ) );
        Assert.Equal( "T:B", reader.GetIdentifier( reader.Annotation.AnnotationClass ) );
    }

    [Fact]
    public void ALiteralAttributeIsNotANameOfTheTable()
    {
        // Instance/@Description is written literally: #1=x neither defines nor refers to a name.
        using var reader = V1TestHelper.CreateReader(
            $"""<Symbols xmlns="{_namespace}"><Class Class="#1=T:A"><Instance Declaration="T:C" Description="#1=x" /></Class><Annotation Target="#1" /></Symbols>""" );

        Assert.True( reader.Read() );
        Assert.True( reader.Read() );
        Assert.Equal( "#1=x", reader.Predecessor.Description );

        // The ends of the predecessor and of the aspect class, then the annotation.
        Assert.Equal( [IntrospectionNodeKind.EndPredecessor, IntrospectionNodeKind.EndAspectClass, IntrospectionNodeKind.Annotation], ReadKinds( reader, 3 ) );
        Assert.Equal( "T:A", reader.GetIdentifier( reader.Annotation.TargetDeclaration ) );
    }

    [Fact]
    public void AnUndefinedNumericValueOfAnEnumerationIsIgnored()
    {
        using var reader = V1TestHelper.CreateReader(
            $"""<Symbols xmlns="{_namespace}"><Evolution Source="M:A" Target="M:B" Kind="999" /><Class Class="T:A"><Instance Declaration="T:C" Origin="999" /></Class></Symbols>""" );

        Assert.True( reader.Read() );
        Assert.Null( reader.DeclarationMapping.Kind );
        Assert.True( reader.Read() );
        Assert.True( reader.Read() );
        Assert.Null( reader.Predecessor.Kind );
    }

    private static IntrospectionNodeKind[] ReadKinds( IntrospectionV1Reader reader, int count )
    {
        var kinds = new IntrospectionNodeKind[count];

        for ( var i = 0; i < count; i++ )
        {
            Assert.True( reader.Read() );
            kinds[i] = reader.NodeKind;
        }

        return kinds;
    }

    private static IntrospectionNodeKind[] ReadAll( IntrospectionV1Reader reader )
    {
        var kinds = new System.Collections.Generic.List<IntrospectionNodeKind>();

        while ( reader.Read() )
        {
            kinds.Add( reader.NodeKind );
        }

        return kinds.ToArray();
    }
}
