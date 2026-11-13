// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Aspects.Introspection.V2;
using System;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;

namespace SharpCrafters.Aspects.Introspection.Tests;

public sealed class IntrospectionV2ReaderTests
{
    [Fact]
    public void TheSampleIsReadNodeByNode()
    {
        var lines = V2TestHelper.Describe( File.ReadAllText( V2TestHelper.SampleFile ) );

        Assert.Equal(
            [
                "Header 2 Metalama 2027.0.8 Sample net10.0 True",
                "AspectClass T:Sample.LogAttribute Log Writes a message to the console when a method starts.",
                "AspectLayer  0",
                "AspectLayer Build 1",
                "AspectInstance 1 M:Sample.Calculator.Add(System.Int32,System.Int32) Calculator.cs(8,5) False",
                "Predecessor Attribute 0 M:Sample.Calculator.Add(System.Int32,System.Int32) T:Sample.LogAttribute  Calculator.cs(7,6)",
                "Transformation OverrideMember OverrideMethod  1 M:Sample.Calculator.Add(System.Int32,System.Int32) - Override the 'Calculator.Add(int, int)' method by 'LogAttribute.OverrideMethod()'.",
                "Transformation IntroduceMember IntroduceField Build 2 T:Sample.Calculator F:Sample.Calculator._logger Introduce the field 'Calculator._logger'.",
                "EndAspectInstance",
                "AspectInstance 2 M:Sample.Calculator.Divide(System.Int32,System.Int32) - True",
                "Predecessor Fabric 0 T:Sample.Fabric -  -",
                "Diagnostic LOG001 Warning The method 'Divide' cannot be logged. M:Sample.Calculator.Divide(System.Int32,System.Int32) Calculator.cs(12,)",
                "EndAspectInstance",
                "EndAspectClass",
                "AspectClass [Sample.Aspects]T:Sample.Aspects.NotifyPropertyChangedAttribute NotifyPropertyChanged ",
                "AspectInstance 3 T:Sample.Customer - False",
                "Predecessor Inherited 0 [Sample.Aspects]T:Sample.Aspects.EntityBase -  -",
                "Transformation ImplementInterface ImplementInterface  0 T:Sample.Customer T:System.ComponentModel.INotifyPropertyChanged ",
                "EndAspectInstance",
                "AspectInstance 4 P:Sample.Customer.Name - False",
                "Predecessor ChildAspect 1 - - 3 -",
                "Transformation OverrideMember OverrideFieldOrPropertyOrIndexer  0 P:Sample.Customer.Name - Override the 'Customer.Name' property by 'NotifyPropertyChangedAttribute.OverrideProperty'.",
                "EndAspectInstance",
                "EndAspectClass"
            ],
            lines );
    }

    [Fact]
    public void AFileWithoutAspectClassesHasNoNode()
    {
        using var reader = V2TestHelper.CreateReader( """{ "schemaVersion": 2, "product": "Metalama" }""" );

        Assert.Equal( "Metalama", reader.Header.Product );
        Assert.True( reader.Header.CompilationSucceeded );
        Assert.False( reader.Read() );
        Assert.Equal( IntrospectionV2NodeKind.None, reader.NodeKind );
        Assert.False( reader.Read() );
    }

    [Fact]
    public void AnEmptyObjectEndsBeforeItsLists()
    {
        var lines = V2TestHelper.Describe(
            """{ "schemaVersion": 2, "product": "Metalama", "aspectClasses": [ { "id": "T:A" }, { "id": "T:B", "aspectInstances": [] } ] }""" );

        Assert.Equal( ["Header 2 Metalama    True", "AspectClass T:A  ", "EndAspectClass", "AspectClass T:B  ", "EndAspectClass"], lines );
    }

    [Fact]
    public void TheSameIdentifierGivesTheSameHandle()
    {
        using var reader = V2TestHelper.CreateReader( File.ReadAllText( V2TestHelper.SampleFile ) );

        reader.Read();
        reader.Read();
        reader.Read();
        reader.Read();
        var aspectInstance = reader.AspectInstance;
        reader.Read();
        var predecessor = reader.Predecessor;

        Assert.Equal( aspectInstance.TargetDeclaration, predecessor.Declaration );
        Assert.Same( reader.GetIdentifier( aspectInstance.TargetDeclaration ), reader.GetIdentifier( predecessor.Declaration ) );
    }

    [Fact]
    public void UnknownPropertiesAreIgnored()
    {
        var lines = V2TestHelper.Describe(
            """
            {
              "schemaVersion": 2,
              "future": { "a": [ 1, 2, { "b": null } ] },
              "product": "Metalama",
              "aspectClasses": [
                {
                  "id": "T:A",
                  "future": [ { "x": 1 } ],
                  "aspectInstances": [
                    {
                      "id": 1,
                      "targetDeclaration": "M:C.M",
                      "secondaryInstances": [ { "id": 2 } ],
                      "predecessors": [ { "kind": "Attribute", "future": true } ],
                      "transformedFile": "obj/C.cs"
                    }
                  ],
                  "displayName": "After the lists"
                }
              ],
              "assembly": "After the aspect classes"
            }
            """ );

        Assert.Equal(
            ["Header 2 Metalama    True", "AspectClass T:A  ", "AspectInstance 1 M:C.M - False", "Predecessor Attribute 0 - -  -", "EndAspectInstance", "EndAspectClass"],
            lines );
    }

    [Fact]
    public void CommentsAndAByteOrderMarkAreAllowed()
    {
        var bytes = Encoding.UTF8.GetPreamble().Concat( Encoding.UTF8.GetBytes( "// Comment.\n{ /* Comment. */ \"schemaVersion\": 2, \"product\": \"Metalama\" }\n" ) ).ToArray();

        using var reader = IntrospectionV2Reader.Create( new MemoryStream( bytes ) );

        Assert.Equal( 2, reader.SchemaVersion );
        Assert.False( reader.Read() );
    }

    [Fact]
    public void ALaterSchemaVersionIsRead()
    {
        using var reader = V2TestHelper.CreateReader( """{ "schemaVersion": 3, "product": "Metalama" }""" );

        Assert.Equal( 3, reader.SchemaVersion );
    }

    [Fact]
    public void AnEarlierSchemaVersionIsRejected()
    {
        var exception = Assert.Throws<IntrospectionFormatException>( () => V2TestHelper.CreateReader( """{ "schemaVersion": 1, "product": "Metalama" }""" ) );

        Assert.Equal( 1, exception.LineNumber );
    }

    [Fact]
    public void TheSchemaVersionIsRequired()
        => Assert.Throws<IntrospectionFormatException>( () => V2TestHelper.CreateReader( """{ "product": "Metalama" }""" ) );

    [Fact]
    public void TheProductIsRequired()
        => Assert.Throws<IntrospectionFormatException>( () => V2TestHelper.CreateReader( """{ "schemaVersion": 2 }""" ) );

    [Fact]
    public void AFileThatIsNotAnObjectIsRejected()
        => Assert.Throws<IntrospectionFormatException>( () => V2TestHelper.CreateReader( "[]" ) );

    [Fact]
    public void InvalidJsonGivesItsLine()
    {
        using var reader = V2TestHelper.CreateReader(
            """
            {
              "schemaVersion": 2,
              "product": "Metalama",
              "aspectClasses": [
                { "id": "T:A" },
                { "id": "T:B" ]
            }
            """ );

        Assert.True( reader.Read() );
        Assert.True( reader.Read() );

        var exception = Assert.Throws<IntrospectionFormatException>( () => reader.Read() );

        Assert.Equal( 6, exception.LineNumber );
        Assert.IsAssignableFrom<System.Text.Json.JsonException>( exception.InnerException );
    }

    [Fact]
    public void AMissingRequiredPropertyGivesItsLine()
    {
        using var reader = V2TestHelper.CreateReader(
            """
            {
              "schemaVersion": 2,
              "product": "Metalama",
              "aspectClasses": [
                {
                  "id": "T:A",
                  "aspectInstances": [
                    { "id": 1 }
                  ]
                }
              ]
            }
            """ );

        Assert.True( reader.Read() );

        var exception = Assert.Throws<IntrospectionFormatException>( () => reader.Read() );

        Assert.Equal( 8, exception.LineNumber );
        Assert.Equal( 9, exception.LinePosition );
        Assert.Contains( "targetDeclaration", exception.Message, StringComparison.Ordinal );
    }

    [Fact]
    public void AnUnknownPredecessorKindIsRejected()
    {
        using var reader = V2TestHelper.CreateReader(
            """{ "schemaVersion": 2, "product": "M", "aspectClasses": [ { "id": "T:A", "aspectInstances": [ { "id": 1, "targetDeclaration": "M:C.M", "predecessors": [ { "kind": "Telepathy" } ] } ] } ] }""" );

        reader.Read();
        reader.Read();

        Assert.Throws<IntrospectionFormatException>( () => reader.Read() );
    }

    [Fact]
    public void AValueOfTheWrongTypeIsRejected()
        => Assert.Throws<IntrospectionFormatException>( () => V2TestHelper.CreateReader( """{ "schemaVersion": "2", "product": "M" }""" ) );

    [Fact]
    public void AFileThatEndsEarlyIsRejected()
    {
        using var reader = V2TestHelper.CreateReader( """{ "schemaVersion": 2, "product": "M", "aspectClasses": [ { "id": "T:A" }""" );

        Assert.Throws<IntrospectionFormatException>(
            () =>
            {
                while ( reader.Read() ) { }
            } );
    }

    [Fact]
    public void TheDataOfAnotherNodeKindIsNotAvailable()
    {
        using var reader = V2TestHelper.CreateReader( File.ReadAllText( V2TestHelper.SampleFile ) );

        reader.Read();

        Assert.Throws<InvalidOperationException>( () => reader.AspectInstance );
    }

    [Fact]
    public void AHandleOfAnotherReaderIsRejected()
    {
        using var first = V2TestHelper.CreateReader( File.ReadAllText( V2TestHelper.SampleFile ) );
        using var second = V2TestHelper.CreateReader( File.ReadAllText( V2TestHelper.SampleFile ) );

        first.Read();

        Assert.Throws<ArgumentException>( () => second.GetIdentifier( first.AspectClass.AspectClass ) );
        Assert.Throws<ArgumentException>( () => second.GetIdentifier( default ) );
    }

    [Fact]
    public void AReaderCannotBeUsedAfterDispose()
    {
        var reader = V2TestHelper.CreateReader( File.ReadAllText( V2TestHelper.SampleFile ) );
        reader.Dispose();

        Assert.Throws<ObjectDisposedException>( () => reader.Read() );
    }
}
