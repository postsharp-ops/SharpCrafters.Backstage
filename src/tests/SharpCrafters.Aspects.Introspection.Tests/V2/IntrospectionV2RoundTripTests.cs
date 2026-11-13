// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Aspects.Introspection.V2;
using System.IO;
using Xunit;

namespace SharpCrafters.Aspects.Introspection.Tests.V2;

public sealed class IntrospectionV2RoundTripTests
{
    [Fact]
    public void TheSampleIsWrittenAgainWithTheSameText()
    {
        var json = File.ReadAllText( V2TestHelper.SampleFile ).ToLf();

        Assert.Equal( json, V2TestHelper.RoundTrip( json ) );
    }

    [Fact]
    public void AWrittenFileIsReadWithTheSameContent()
    {
        var json = V2TestHelper.Write(
            writer =>
            {
                var aspect = writer.Declare( "T:A" );
                var method = writer.Declare( "M:C.M(System.Collections.Generic.List{System.Int32})" );

                writer.WriteStartAspectClass( new V2AspectClassData { AspectClass = aspect, DisplayName = "A", Description = "Ä \"quoted\"\n" } );
                writer.WriteAspectLayer( new V2AspectLayerData { Name = "L", Order = 3 } );
                writer.WriteStartAspectInstance( new V2AspectInstanceData { Id = 5, TargetDeclaration = method, IsSkipped = true } );
                writer.WritePredecessor( new V2PredecessorData { Kind = V2PredecessorKind.RequiredAspect, Degree = 2, AspectInstance = 9 } );
                writer.WriteTransformation( new V2TransformationData { Kind = "InsertStatement", Layer = "L", Order = 7, TargetDeclaration = method } );
                writer.WriteDiagnostic( new V2DiagnosticData { Id = "D", Severity = V2DiagnosticSeverity.Hidden, Declaration = aspect } );
                writer.WriteEndAspectInstance();
                writer.WriteEndAspectClass();
            } );

        Assert.Equal(
            [
                "Header 2 Metalama    True",
                "AspectClass T:A A Ä \"quoted\"\n",
                "AspectLayer L 3",
                "AspectInstance 5 M:C.M(System.Collections.Generic.List{System.Int32}) - True",
                "Predecessor RequiredAspect 2 - - 9 -",
                "Transformation InsertStatement  L 7 M:C.M(System.Collections.Generic.List{System.Int32}) - ",
                "Diagnostic D Hidden  T:A -",
                "EndAspectInstance",
                "EndAspectClass"
            ],
            V2TestHelper.Describe( json ) );

        Assert.Equal( json, V2TestHelper.RoundTrip( json ) );
    }
}
