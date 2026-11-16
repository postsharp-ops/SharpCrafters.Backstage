// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Aspects.Introspection.V2;
using System;
using System.IO;
using System.Text.Json;
using Xunit;

namespace SharpCrafters.Aspects.Introspection.Tests.V2;

/// <summary>
/// Tests of <see cref="IntrospectionV2Writer"/>: the JSON that it writes, the values that it omits, and the calls that it
/// rejects.
/// </summary>
public sealed class IntrospectionV2WriterTests
{
    /// <summary>
    /// The options of the tests that compare the whole output: not indented, so that the expected JSON fits on one line, and
    /// without the escaping of the characters that are safe in a file.
    /// </summary>
    private static readonly JsonWriterOptions _compact = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>
    /// Checks that a file without aspect classes has only the properties of the root object.
    /// </summary>
    [Fact]
    public void AFileWithoutAspectClassesHasOnlyTheRootProperties()
    {
        var json = V2TestHelper.Write( _ => { }, options: _compact );

        Assert.Equal( """{"schemaVersion":2,"product":"Metalama","compilationSucceeded":true}""", json );
    }

    /// <summary>
    /// Checks that the optional properties of the root object are written when they are set.
    /// </summary>
    [Fact]
    public void TheOptionalRootPropertiesAreWrittenWhenSet()
    {
        var json = V2TestHelper.Write(
            _ => { },
            new V2HeaderData { Product = "Metalama", ProductVersion = "2027.0.8", Assembly = "A", TargetFramework = "net10.0", CompilationSucceeded = false },
            _compact );

        Assert.Equal(
            """{"schemaVersion":2,"product":"Metalama","productVersion":"2027.0.8","assembly":"A","targetFramework":"net10.0","compilationSucceeded":false}""",
            json );
    }

    /// <summary>
    /// Checks that the writer omits the implied values: <c>false</c>, <c>0</c>, and the optional properties that have no value.
    /// </summary>
    [Fact]
    public void ImpliedValuesAreOmitted()
    {
        var json = V2TestHelper.Write(
            writer =>
            {
                var aspect = writer.Declare( "T:A" );
                var method = writer.Declare( "M:C.M" );

                writer.WriteStartAspectClass( new V2AspectClassData { AspectClass = aspect } );
                writer.WriteAspectLayer( default );
                writer.WriteStartAspectInstance( new V2AspectInstanceData { Id = 1, TargetDeclaration = method } );
                writer.WritePredecessor( new V2PredecessorData { Kind = V2PredecessorKind.Attribute } );
                writer.WriteTransformation( new V2TransformationData { Kind = "OverrideMember", TargetDeclaration = method } );
                writer.WriteEndAspectInstance();
                writer.WriteEndAspectClass();
            },
            options: _compact );

        Assert.Equal(
            """{"schemaVersion":2,"product":"Metalama","compilationSucceeded":true,"aspectClasses":[{"id":"T:A","layers":[{}],"aspectInstances":[{"id":1,"targetDeclaration":"M:C.M","predecessors":[{"kind":"Attribute"}],"transformations":[{"kind":"OverrideMember","targetDeclaration":"M:C.M"}]}]}]}""",
            json );
    }

    /// <summary>
    /// Checks that the items of a list are written in one array, and that an object ends its open array.
    /// </summary>
    [Fact]
    public void TheLinesOfAnObjectAreGroupedInArrays()
    {
        var json = V2TestHelper.Write(
            writer =>
            {
                var aspect = writer.Declare( "T:A" );
                var method = writer.Declare( "M:C.M" );

                writer.WriteStartAspectClass( new V2AspectClassData { AspectClass = aspect } );
                writer.WriteStartAspectInstance( new V2AspectInstanceData { Id = 1, TargetDeclaration = method } );
                writer.WriteDiagnostic( new V2DiagnosticData { Id = "X1", Severity = V2DiagnosticSeverity.Error } );
                writer.WriteDiagnostic( new V2DiagnosticData { Id = "X2", Severity = V2DiagnosticSeverity.Info } );
                writer.WriteEndAspectInstance();
                writer.WriteStartAspectInstance( new V2AspectInstanceData { Id = 2, TargetDeclaration = method, IsSkipped = true } );
                writer.WriteEndAspectInstance();
                writer.WriteEndAspectClass();
            },
            options: _compact );

        Assert.Equal(
            """{"schemaVersion":2,"product":"Metalama","compilationSucceeded":true,"aspectClasses":[{"id":"T:A","aspectInstances":[{"id":1,"targetDeclaration":"M:C.M","diagnostics":[{"id":"X1","severity":"Error"},{"id":"X2","severity":"Info"}]},{"id":2,"targetDeclaration":"M:C.M","isSkipped":true}]}]}""",
            json );
    }

    /// <summary>
    /// Checks that <see cref="IntrospectionV2Writer.Dispose"/> ends the arrays and objects that are still open, so that the
    /// output is valid JSON.
    /// </summary>
    [Fact]
    public void DisposeClosesTheOpenObjects()
    {
        var json = V2TestHelper.Write(
            writer =>
            {
                writer.WriteStartAspectClass( new V2AspectClassData { AspectClass = writer.Declare( "T:A" ) } );
                writer.WriteStartAspectInstance( new V2AspectInstanceData { Id = 1, TargetDeclaration = writer.Declare( "M:C.M" ) } );
                writer.WritePredecessor( new V2PredecessorData { Kind = V2PredecessorKind.Fabric } );
            },
            options: _compact );

        Assert.Equal(
            """{"schemaVersion":2,"product":"Metalama","compilationSucceeded":true,"aspectClasses":[{"id":"T:A","aspectInstances":[{"id":1,"targetDeclaration":"M:C.M","predecessors":[{"kind":"Fabric"}]}]}]}""",
            json );

        using var document = JsonDocument.Parse( json );
    }

    /// <summary>
    /// Checks that the identifier of a key is formatted once, that each key has its own handle, and that a <c>null</c> key gives
    /// a nil handle.
    /// </summary>
    [Fact]
    public void TheIdentifierIsFormattedOncePerKey()
    {
        var count = 0;
        var key = new object();

        V2TestHelper.Write(
            writer =>
            {
                string Format( object _ )
                {
                    count++;

                    return "T:A";
                }

                var first = writer.GetDeclarationHandle( key, Format );
                var second = writer.GetDeclarationHandle( key, Format );
                var other = writer.GetDeclarationHandle( new object(), Format );

                Assert.Equal( first, second );
                Assert.NotEqual( first, other );
                Assert.True( writer.GetDeclarationHandle<object>( null, Format ).IsNil );
            } );

        Assert.Equal( 2, count );
    }

    /// <summary>
    /// Checks that a location is written with its line and column, and that a column without a line is not written.
    /// </summary>
    [Fact]
    public void ALocationIsWrittenWithItsLineAndColumn()
    {
        var json = V2TestHelper.Write(
            writer =>
            {
                writer.WriteStartAspectClass( new V2AspectClassData { AspectClass = writer.Declare( "T:A" ) } );

                writer.WriteStartAspectInstance(
                    new V2AspectInstanceData
                    {
                        Id = 1, TargetDeclaration = writer.Declare( "M:C.M" ), Location = new V2SourceLocation { File = "C.cs", Column = 3 }
                    } );

                writer.WritePredecessor(
                    new V2PredecessorData
                    {
                        Kind = V2PredecessorKind.Attribute, Location = new V2SourceLocation { File = "C.cs", Line = 4, Column = 5 }
                    } );
            },
            options: _compact );

        // The column is written only with the line.
        Assert.DoesNotContain( "endLine", json, StringComparison.Ordinal );
        Assert.Contains( "\"location\":{\"file\":\"C.cs\"},", json, StringComparison.Ordinal );
        Assert.Contains( "\"location\":{\"file\":\"C.cs\",\"line\":4,\"column\":5}", json, StringComparison.Ordinal );
    }

    /// <summary>
    /// Checks that the end line of a location is written after its column and read back.
    /// </summary>
    [Fact]
    public void TheEndLineIsWrittenAfterTheColumnAndReadBack()
    {
        var json = V2TestHelper.Write(
            writer =>
            {
                writer.WriteStartAspectClass( new V2AspectClassData { AspectClass = writer.Declare( "T:A" ) } );

                writer.WriteStartAspectInstance(
                    new V2AspectInstanceData
                    {
                        Id = 1,
                        TargetDeclaration = writer.Declare( "M:C.M" ),
                        Location = new V2SourceLocation { File = "C.cs", Line = 4, Column = 5, EndLine = 9 }
                    } );
            },
            options: _compact );

        Assert.Contains( "\"location\":{\"file\":\"C.cs\",\"line\":4,\"column\":5,\"endLine\":9}", json, StringComparison.Ordinal );

        using var reader = V2TestHelper.CreateReader( json );
        reader.Read();
        reader.Read();
        Assert.Equal( 9, reader.AspectInstance.Location?.EndLine );
    }

    /// <summary>
    /// Checks that a predecessor cannot be written after a transformation of the same aspect instance.
    /// </summary>
    [Fact]
    public void ThePredecessorsComeBeforeTheTransformations()
        => AssertInvalidOrder(
            writer =>
            {
                writer.WriteTransformation( new V2TransformationData { Kind = "OverrideMember", TargetDeclaration = writer.Declare( "M:C.M" ) } );
                writer.WritePredecessor( new V2PredecessorData { Kind = V2PredecessorKind.Attribute } );
            } );

    /// <summary>
    /// Checks that a transformation cannot be written after a diagnostic of the same aspect instance.
    /// </summary>
    [Fact]
    public void TheTransformationsComeBeforeTheDiagnostics()
        => AssertInvalidOrder(
            writer =>
            {
                writer.WriteDiagnostic( new V2DiagnosticData { Id = "X", Severity = V2DiagnosticSeverity.Warning } );
                writer.WriteTransformation( new V2TransformationData { Kind = "OverrideMember", TargetDeclaration = writer.Declare( "M:C.M" ) } );
            } );

    /// <summary>
    /// Checks that an aspect layer cannot be written after an aspect instance of the same aspect class.
    /// </summary>
    [Fact]
    public void TheAspectLayersComeBeforeTheAspectInstances()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => V2TestHelper.Write(
                writer =>
                {
                    writer.WriteStartAspectClass( new V2AspectClassData { AspectClass = writer.Declare( "T:A" ) } );
                    writer.WriteStartAspectInstance( new V2AspectInstanceData { Id = 1, TargetDeclaration = writer.Declare( "M:C.M" ) } );
                    writer.WriteEndAspectInstance();
                    writer.WriteAspectLayer( default );
                } ) );

        Assert.Contains( "AspectInstances", exception.Message, StringComparison.Ordinal );
    }

    /// <summary>
    /// Checks that a predecessor cannot be written directly inside an aspect class, and that the message names both levels.
    /// </summary>
    [Fact]
    public void APredecessorBelongsInsideAnAspectInstance()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => V2TestHelper.Write(
                writer =>
                {
                    writer.WriteStartAspectClass( new V2AspectClassData { AspectClass = writer.Declare( "T:A" ) } );
                    writer.WritePredecessor( new V2PredecessorData { Kind = V2PredecessorKind.Attribute } );
                } ) );

        Assert.Equal( "Cannot write a predecessor inside an aspect class. It belongs inside an aspect instance.", exception.Message );
    }

    /// <summary>
    /// Checks that the end of an aspect instance cannot be written when no aspect instance is open.
    /// </summary>
    [Fact]
    public void AnEndWithoutStartThrows()
        => Assert.Throws<InvalidOperationException>( () => V2TestHelper.Write( writer => writer.WriteEndAspectInstance() ) );

    /// <summary>
    /// Checks that two aspect instances of a file cannot have the same identifier, even in two aspect classes.
    /// </summary>
    [Fact]
    public void TheIdentifiersOfTheAspectInstancesAreUnique()
        => Assert.Throws<ArgumentException>(
            () => V2TestHelper.Write(
                writer =>
                {
                    var method = writer.Declare( "M:C.M" );
                    writer.WriteStartAspectClass( new V2AspectClassData { AspectClass = writer.Declare( "T:A" ) } );
                    writer.WriteStartAspectInstance( new V2AspectInstanceData { Id = 1, TargetDeclaration = method } );
                    writer.WriteEndAspectInstance();
                    writer.WriteEndAspectClass();
                    writer.WriteStartAspectClass( new V2AspectClassData { AspectClass = writer.Declare( "T:B" ) } );
                    writer.WriteStartAspectInstance( new V2AspectInstanceData { Id = 1, TargetDeclaration = method } );
                } ) );

    /// <summary>
    /// Checks that the identifier of an aspect instance cannot be zero.
    /// </summary>
    [Fact]
    public void TheIdentifierOfAnAspectInstanceIsPositive()
        => Assert.Throws<ArgumentException>(
            () => V2TestHelper.Write(
                writer =>
                {
                    writer.WriteStartAspectClass( new V2AspectClassData { AspectClass = writer.Declare( "T:A" ) } );
                    writer.WriteStartAspectInstance( new V2AspectInstanceData { Id = 0, TargetDeclaration = writer.Declare( "M:C.M" ) } );
                } ) );

    /// <summary>
    /// Checks that a required declaration cannot be a nil handle, and that the exception names the property.
    /// </summary>
    [Fact]
    public void ARequiredDeclarationMustBeSet()
    {
        var exception = Assert.Throws<ArgumentException>(
            () => V2TestHelper.Write( writer => writer.WriteStartAspectClass( default ) ) );

        Assert.Equal( nameof(V2AspectClassData.AspectClass), exception.ParamName );
    }

    /// <summary>
    /// Checks that a required string cannot be empty.
    /// </summary>
    [Fact]
    public void ARequiredStringMustBeSet()
        => Assert.Throws<ArgumentException>(
            () => V2TestHelper.Write(
                writer =>
                {
                    writer.WriteStartAspectClass( new V2AspectClassData { AspectClass = writer.Declare( "T:A" ) } );
                    writer.WriteStartAspectInstance( new V2AspectInstanceData { Id = 1, TargetDeclaration = writer.Declare( "M:C.M" ) } );
                    writer.WriteTransformation( new V2TransformationData { Kind = "", TargetDeclaration = writer.Declare( "M:C.M" ) } );
                } ) );

    /// <summary>
    /// Checks that the writer cannot be created without a product.
    /// </summary>
    [Fact]
    public void TheProductMustBeSet()
        => Assert.Throws<ArgumentException>( () => new IntrospectionV2Writer( new Utf8JsonWriter( new MemoryStream() ), default ) );

    /// <summary>
    /// Checks that a handle returned by another writer is rejected.
    /// </summary>
    [Fact]
    public void AHandleOfAnotherWriterIsRejected()
    {
        DeclarationHandle foreign = default;
        V2TestHelper.Write( writer => foreign = writer.Declare( "T:A" ) );

        Assert.Throws<ArgumentException>(
            () => V2TestHelper.Write( writer => writer.WriteStartAspectClass( new V2AspectClassData { AspectClass = foreign } ) ) );
    }

    /// <summary>
    /// Checks that a value that its enumeration does not define is rejected.
    /// </summary>
    [Fact]
    public void AnUndefinedEnumValueIsRejected()
        => Assert.Throws<ArgumentException>(
            () => V2TestHelper.Write(
                writer =>
                {
                    writer.WriteStartAspectClass( new V2AspectClassData { AspectClass = writer.Declare( "T:A" ) } );
                    writer.WriteStartAspectInstance( new V2AspectInstanceData { Id = 1, TargetDeclaration = writer.Declare( "M:C.M" ) } );
                    writer.WritePredecessor( new V2PredecessorData { Kind = (V2PredecessorKind) 42 } );
                } ) );

    /// <summary>
    /// Checks that the characters that the encoder of Metalama allows, such as <c>&lt;</c> and <c>'</c>, are written as they are.
    /// </summary>
    [Fact]
    public void TheWriterDoesNotEscapeTheCharactersThatTheEncoderAllows()
    {
        var json = V2TestHelper.Write(
            writer => writer.WriteStartAspectClass( new V2AspectClassData { AspectClass = writer.Declare( "T:A`1" ), Description = "List<T> & 'x'" } ) );

        Assert.Contains( "\"description\": \"List<T> & 'x'\"", json, StringComparison.Ordinal );
        Assert.Contains( "\"id\": \"T:A`1\"", json, StringComparison.Ordinal );
    }

    /// <summary>
    /// Asserts that the given calls, made inside an aspect instance, throw an <see cref="InvalidOperationException"/>.
    /// </summary>
    private static void AssertInvalidOrder( Action<IntrospectionV2Writer> writeInsideAspectInstance )
        => Assert.Throws<InvalidOperationException>(
            () => V2TestHelper.Write(
                writer =>
                {
                    writer.WriteStartAspectClass( new V2AspectClassData { AspectClass = writer.Declare( "T:A" ) } );
                    writer.WriteStartAspectInstance( new V2AspectInstanceData { Id = 1, TargetDeclaration = writer.Declare( "M:C.M" ) } );
                    writeInsideAspectInstance( writer );
                } ) );
}
