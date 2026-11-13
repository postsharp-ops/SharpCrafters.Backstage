// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Aspects.Introspection.V2;
using System;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;

namespace SharpCrafters.Aspects.Introspection.Tests.V2;

/// <summary>
/// Tests of <see cref="IntrospectionV2Reader"/>: the nodes and values that it reads, the properties that it ignores, and the
/// errors that it reports for malformed files.
/// </summary>
public sealed class IntrospectionV2ReaderTests
{
    /// <summary>
    /// Checks that the fixture <c>Sample.mlsym</c> is read node by node, with the values of every kind of node.
    /// </summary>
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

    /// <summary>
    /// Checks that a file without aspect classes gives its header and no node, and that <see cref="IntrospectionV2Reader.Read"/>
    /// keeps returning <c>false</c> at the end.
    /// </summary>
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

    /// <summary>
    /// Checks that an aspect class without lists, or with an empty list, gives its start and its end.
    /// </summary>
    [Fact]
    public void AnEmptyObjectEndsBeforeItsLists()
    {
        var lines = V2TestHelper.Describe(
            """{ "schemaVersion": 2, "product": "Metalama", "aspectClasses": [ { "id": "T:A" }, { "id": "T:B", "aspectInstances": [] } ] }""" );

        Assert.Equal( ["Header 2 Metalama    True", "AspectClass T:A  ", "EndAspectClass", "AspectClass T:B  ", "EndAspectClass"], lines );
    }

    /// <summary>
    /// Checks that two uses of one identifier give the same handle and the same string instance.
    /// </summary>
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

    /// <summary>
    /// Checks that unknown properties, unknown lists, and properties that come after the lists of an object are ignored.
    /// </summary>
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

    /// <summary>
    /// Checks that JSON comments and a UTF-8 byte order mark are accepted.
    /// </summary>
    [Fact]
    public void CommentsAndAByteOrderMarkAreAllowed()
    {
        var bytes = Encoding.UTF8.GetPreamble().Concat( Encoding.UTF8.GetBytes( "// Comment.\n{ /* Comment. */ \"schemaVersion\": 2, \"product\": \"Metalama\" }\n" ) ).ToArray();

        using var reader = IntrospectionV2Reader.Create( new MemoryStream( bytes ) );

        Assert.Equal( 2, reader.SchemaVersion );
        Assert.False( reader.Read() );
    }

    /// <summary>
    /// Checks that a file of a schema version above 2 is read, and that its version is exposed.
    /// </summary>
    [Fact]
    public void ALaterSchemaVersionIsRead()
    {
        using var reader = V2TestHelper.CreateReader( """{ "schemaVersion": 3, "product": "Metalama" }""" );

        Assert.Equal( 3, reader.SchemaVersion );
    }

    /// <summary>
    /// Checks that a file of a schema version below 2 is rejected, with the line of the root object.
    /// </summary>
    [Fact]
    public void AnEarlierSchemaVersionIsRejected()
    {
        var exception = Assert.Throws<IntrospectionFormatException>( () => V2TestHelper.CreateReader( """{ "schemaVersion": 1, "product": "Metalama" }""" ) );

        Assert.Equal( 1, exception.LineNumber );
    }

    /// <summary>
    /// Checks that a file without a schema version is rejected.
    /// </summary>
    [Fact]
    public void TheSchemaVersionIsRequired()
        => Assert.Throws<IntrospectionFormatException>( () => V2TestHelper.CreateReader( """{ "product": "Metalama" }""" ) );

    /// <summary>
    /// Checks that a file without a product is rejected.
    /// </summary>
    [Fact]
    public void TheProductIsRequired()
        => Assert.Throws<IntrospectionFormatException>( () => V2TestHelper.CreateReader( """{ "schemaVersion": 2 }""" ) );

    /// <summary>
    /// Checks that an <c>aspectClasses</c> property that is not an array is rejected.
    /// </summary>
    [Fact]
    public void AnAspectClassesPropertyThatIsNotAnArrayIsRejected()
        => Assert.Throws<IntrospectionFormatException>(
            () => V2TestHelper.CreateReader( """{ "schemaVersion": 2, "product": "M", "aspectClasses": { "id": "T:A" } }""" ) );

    /// <summary>
    /// Checks that an <c>aspectClasses</c> property whose value is <c>null</c> is read as an empty list.
    /// </summary>
    [Fact]
    public void ANullAspectClassesPropertyIsAnEmptyList()
    {
        using var reader = V2TestHelper.CreateReader( """{ "schemaVersion": 2, "product": "M", "aspectClasses": null }""" );

        Assert.False( reader.Read() );
    }

    /// <summary>
    /// Checks that a file whose root is not a JSON object is rejected.
    /// </summary>
    [Fact]
    public void AFileThatIsNotAnObjectIsRejected()
        => Assert.Throws<IntrospectionFormatException>( () => V2TestHelper.CreateReader( "[]" ) );

    /// <summary>
    /// Checks that invalid JSON is reported with its line, which is counted from the start of the file and not from the start of
    /// the current read.
    /// </summary>
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

    /// <summary>
    /// Checks that an object without a required property is reported with the line and the position of the object.
    /// </summary>
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

    /// <summary>
    /// Checks that a predecessor kind that the format does not define is rejected.
    /// </summary>
    [Fact]
    public void AnUnknownPredecessorKindIsRejected()
    {
        using var reader = V2TestHelper.CreateReader(
            """{ "schemaVersion": 2, "product": "M", "aspectClasses": [ { "id": "T:A", "aspectInstances": [ { "id": 1, "targetDeclaration": "M:C.M", "predecessors": [ { "kind": "Telepathy" } ] } ] } ] }""" );

        reader.Read();
        reader.Read();

        Assert.Throws<IntrospectionFormatException>( () => reader.Read() );
    }

    /// <summary>
    /// Checks that a property whose value has the wrong JSON type is rejected.
    /// </summary>
    [Fact]
    public void AValueOfTheWrongTypeIsRejected()
        => Assert.Throws<IntrospectionFormatException>( () => V2TestHelper.CreateReader( """{ "schemaVersion": "2", "product": "M" }""" ) );

    /// <summary>
    /// Checks that a file that ends before the end of its root object is rejected.
    /// </summary>
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

    /// <summary>
    /// Checks that the data of a node kind cannot be read while the reader is positioned on another kind.
    /// </summary>
    [Fact]
    public void TheDataOfAnotherNodeKindIsNotAvailable()
    {
        using var reader = V2TestHelper.CreateReader( File.ReadAllText( V2TestHelper.SampleFile ) );

        reader.Read();

        Assert.Throws<InvalidOperationException>( () => reader.AspectInstance );
    }

    /// <summary>
    /// Checks that a handle returned by another reader, and the nil handle, are rejected.
    /// </summary>
    [Fact]
    public void AHandleOfAnotherReaderIsRejected()
    {
        using var first = V2TestHelper.CreateReader( File.ReadAllText( V2TestHelper.SampleFile ) );
        using var second = V2TestHelper.CreateReader( File.ReadAllText( V2TestHelper.SampleFile ) );

        first.Read();

        Assert.Throws<ArgumentException>( () => second.GetIdentifier( first.AspectClass.AspectClass ) );
        Assert.Throws<ArgumentException>( () => second.GetIdentifier( default ) );
    }

    /// <summary>
    /// Checks that a reader cannot read after it is disposed.
    /// </summary>
    [Fact]
    public void AReaderCannotBeUsedAfterDispose()
    {
        var reader = V2TestHelper.CreateReader( File.ReadAllText( V2TestHelper.SampleFile ) );
        reader.Dispose();

        Assert.Throws<ObjectDisposedException>( () => reader.Read() );
    }
}
