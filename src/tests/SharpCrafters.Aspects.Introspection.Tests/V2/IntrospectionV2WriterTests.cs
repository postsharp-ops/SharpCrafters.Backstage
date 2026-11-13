// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Aspects.Introspection.V2;
using System;
using System.IO;
using System.Text.Json;
using Xunit;

namespace SharpCrafters.Aspects.Introspection.Tests.V2;

public sealed class IntrospectionV2WriterTests
{
    private static readonly JsonWriterOptions _compact = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    [Fact]
    public void AFileWithoutAspectClassesHasOnlyTheRootProperties()
    {
        var json = V2TestHelper.Write( _ => { }, options: _compact );

        Assert.Equal( """{"schemaVersion":2,"product":"Metalama","compilationSucceeded":true}""", json );
    }

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

    [Fact]
    public void ThePredecessorsComeBeforeTheTransformations()
        => AssertInvalidOrder(
            writer =>
            {
                writer.WriteTransformation( new V2TransformationData { Kind = "OverrideMember", TargetDeclaration = writer.Declare( "M:C.M" ) } );
                writer.WritePredecessor( new V2PredecessorData { Kind = V2PredecessorKind.Attribute } );
            } );

    [Fact]
    public void TheTransformationsComeBeforeTheDiagnostics()
        => AssertInvalidOrder(
            writer =>
            {
                writer.WriteDiagnostic( new V2DiagnosticData { Id = "X", Severity = V2DiagnosticSeverity.Warning } );
                writer.WriteTransformation( new V2TransformationData { Kind = "OverrideMember", TargetDeclaration = writer.Declare( "M:C.M" ) } );
            } );

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

    [Fact]
    public void AnEndWithoutStartThrows()
        => Assert.Throws<InvalidOperationException>( () => V2TestHelper.Write( writer => writer.WriteEndAspectInstance() ) );

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

    [Fact]
    public void TheIdentifierOfAnAspectInstanceIsPositive()
        => Assert.Throws<ArgumentException>(
            () => V2TestHelper.Write(
                writer =>
                {
                    writer.WriteStartAspectClass( new V2AspectClassData { AspectClass = writer.Declare( "T:A" ) } );
                    writer.WriteStartAspectInstance( new V2AspectInstanceData { Id = 0, TargetDeclaration = writer.Declare( "M:C.M" ) } );
                } ) );

    [Fact]
    public void ARequiredDeclarationMustBeSet()
    {
        var exception = Assert.Throws<ArgumentException>(
            () => V2TestHelper.Write( writer => writer.WriteStartAspectClass( default ) ) );

        Assert.Equal( nameof(V2AspectClassData.AspectClass), exception.ParamName );
    }

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

    [Fact]
    public void TheProductMustBeSet()
        => Assert.Throws<ArgumentException>( () => new IntrospectionV2Writer( new Utf8JsonWriter( new MemoryStream() ), default ) );

    [Fact]
    public void AHandleOfAnotherWriterIsRejected()
    {
        DeclarationHandle foreign = default;
        V2TestHelper.Write( writer => foreign = writer.Declare( "T:A" ) );

        Assert.Throws<ArgumentException>(
            () => V2TestHelper.Write( writer => writer.WriteStartAspectClass( new V2AspectClassData { AspectClass = foreign } ) ) );
    }

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

    [Fact]
    public void TheWriterDoesNotEscapeTheCharactersThatTheEncoderAllows()
    {
        var json = V2TestHelper.Write(
            writer => writer.WriteStartAspectClass( new V2AspectClassData { AspectClass = writer.Declare( "T:A`1" ), Description = "List<T> & 'x'" } ) );

        Assert.Contains( "\"description\": \"List<T> & 'x'\"", json, StringComparison.Ordinal );
        Assert.Contains( "\"id\": \"T:A`1\"", json, StringComparison.Ordinal );
    }

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
