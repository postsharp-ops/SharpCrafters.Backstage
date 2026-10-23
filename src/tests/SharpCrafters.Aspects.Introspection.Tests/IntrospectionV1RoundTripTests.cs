// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace SharpCrafters.Aspects.Introspection.Tests;

/// <summary>
/// Reads files written by PostSharp and writes them again through the data structures. The files are compared element by
/// element, with the effective values, as the port of <c>WriteWeavingSymbolsTask</c> of PostSharp is
/// (postsharp/PostSharp.Public#330).
/// </summary>
/// <remarks>
/// The text is not always the same. <c>WriteWeavingSymbolsTask</c> omits an implied declaration when the object of the
/// compiler is the same, not when the name is: a file of PostSharp can carry an <c>Advising</c> attribute equal to its aspect
/// class. Such an attribute is read as its effective value, and written again as implied.
/// </remarks>
public sealed class IntrospectionV1RoundTripTests
{
    public static IEnumerable<object[]> Fixtures
        => Directory.GetFiles( V1TestHelper.FixturesDirectory, "*.pssym", SearchOption.AllDirectories )
            .Select( f => new object[] { f.Substring( V1TestHelper.FixturesDirectory.Length + 1 ) } );

    [Theory]
    [MemberData( nameof(Fixtures) )]
    public void AFileIsWrittenAgainWithTheSameContent( string relativePath )
    {
        var original = File.ReadAllText( Path.Combine( V1TestHelper.FixturesDirectory, relativePath ) );
        var written = V1TestHelper.RoundTrip( original );

        Assert.Equal( V1TestHelper.Describe( original ), V1TestHelper.Describe( written ) );

        // A file that is written again is written identically.
        Assert.Equal( written, V1TestHelper.RoundTrip( written ) );
    }

    [Fact]
    public void AFileOfPostSharpIsWrittenAgainIdenticallyButForTheExplicitImpliedValues()
    {
        var original = V1TestHelper.Normalize( File.ReadAllText( Path.Combine( V1TestHelper.FixturesDirectory, "PostSharp.Patterns.Aggregation.Tests.pssym" ) ) );
        var written = V1TestHelper.RoundTrip( original );

        // The name table is numbered alike, so the files differ only where WriteWeavingSymbolsTask wrote an implied value: an
        // Advising attribute that names the aspect class through another object of the compiler.
        var originalLines = original.Split( '\n' );
        var writtenLines = written.Split( '\n' );

        Assert.Equal( originalLines.Length, writtenLines.Length );

        for ( var i = 0; i < originalLines.Length; i++ )
        {
            if ( originalLines[i] != writtenLines[i] )
            {
                Assert.Matches( " Advising=\"#\\d+\"", originalLines[i] );
                Assert.Equal( System.Text.RegularExpressions.Regex.Replace( originalLines[i], " Advising=\"#\\d+\"", "" ), writtenLines[i] );
            }
        }
    }

    [Fact]
    public void AWrittenFileIsReadWithTheSameValues()
    {
        DeclarationHandle aspectClass = default, method = default, other = default;

        var xml = V1TestHelper.Write(
            writer =>
            {
                aspectClass = writer.DefineDeclaration( "class", "T:[A]Aspect" );
                method = writer.DefineDeclaration( "method", "M:[A]C::M()" );
                other = writer.DefineDeclaration( "other", "M:[A]C::N()" );

                writer.WriteDeclarationMapping( new DeclarationMappingData { Source = method, Target = other, Kind = DeclarationMappingKind.Wrapped } );
                writer.WriteStartAspectClass( new AspectClassData { AspectClass = aspectClass, HasLimitedLicense = true } );

                writer.WriteStartPredecessor(
                    new PredecessorData
                    {
                        Declaration = method,
                        File = "postsharp.config",
                        Line = 3,
                        Column = 5,
                        IsForwardReference = true,
                        Description = "A description",
                        Id = "1234",
                        Kind = PredecessorKind.Multicast,
                        ProvidedBy = "7"
                    } );

                writer.WriteStartAspectInstance( new AspectInstanceData { TargetDeclaration = other, LinesOfCodeAvoided = 4, Tags = "premium", Id = "8" } );

                writer.WriteTransformation(
                    new TransformationData
                    {
                        TargetDeclaration = method,
                        AdviceDeclaration = other,
                        Description = "Wrapped",
                        Semantic = "Getter",
                        Order = 3,
                        LinesOfCodeAvoided = 0
                    } );

                writer.WriteEndAspectInstance();
                writer.WriteEndPredecessor();
                writer.WriteEndAspectClass();

                writer.WriteAnnotation(
                    new AnnotationData { TargetDeclaration = method, AnnotationClass = aspectClass, Arguments = "2", Description = "Saved", LinesOfCodeAvoided = 2 } );
            },
            new SymbolsData { ProductVersion = "2027.0.1", LinesOfCode = 10, ProductLinesOfCode = 1, EnhancedTypesCount = 2 } );

        Assert.Equal(
            new[]
            {
                "Symbols 1 2027.0.1 10 1 2",
                "Mapping M:[A]C::M() M:[A]C::N() Wrapped",
                "Class T:[A]Aspect True",
                "Predecessor M:[A]C::M() postsharp.config 3 5 True A description 1234 Multicast 7",
                "AspectInstance M:[A]C::N() 4 premium 8",
                "Transformation M:[A]C::M() M:[A]C::N() Wrapped Getter 3 0",
                "EndAspectInstance",
                "EndPredecessor",
                "EndAspectClass",
                "Annotation M:[A]C::M() T:[A]Aspect 2 Saved 2"
            },
            V1TestHelper.Describe( xml ) );
    }
}
