// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace SharpCrafters.Aspects.Introspection.Tests;

internal static class V2TestHelper
{
    public static readonly V2HeaderData DefaultHeader = new() { Product = "Metalama", CompilationSucceeded = true };

    public static string SampleFile => Path.Combine( V1TestHelper.FixturesDirectory, "Metalama", "Sample.mlsym" );

    /// <summary>
    /// The options with which Metalama writes the file: indented, and without the escaping of the characters that are safe in a
    /// file, such as <c>&lt;</c> in a generic type name, so that the file can be searched as text.
    /// </summary>
    public static JsonWriterOptions WriterOptions { get; } = new() { Indented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static string Write( Action<IntrospectionV2Writer> write, V2HeaderData? header = null, JsonWriterOptions? options = null )
    {
        var stream = new MemoryStream();

        using ( var jsonWriter = new Utf8JsonWriter( stream, options ?? WriterOptions ) )
        {
            using var writer = new IntrospectionV2Writer( jsonWriter, header ?? DefaultHeader );
            write( writer );
        }

        return Encoding.UTF8.GetString( stream.ToArray() ).ToLf();
    }

    // A declaration of the tests: each call creates a key of its own, and so a handle of its own.
    public static DeclarationHandle Declare( this IntrospectionV2Writer writer, string identifier )
        => writer.GetDeclarationHandle( new TestDeclaration( identifier ), _formatTestDeclaration );

    private static readonly Func<TestDeclaration, string> _formatTestDeclaration = d => d.Identifier;

    private sealed class TestDeclaration( string identifier )
    {
        public string Identifier { get; } = identifier;
    }

    public static IntrospectionV2Reader CreateReader( string json ) => IntrospectionV2Reader.Create( new MemoryStream( Encoding.UTF8.GetBytes( json ) ) );

    /// <summary>
    /// Describes the content of a file node by node, with the effective values and the identifiers of the declarations. Two files
    /// with the same description have the same content, whatever their formatting and their implied values.
    /// </summary>
    public static List<string> Describe( string json )
    {
        using var reader = CreateReader( json );

        string Id( DeclarationHandle handle ) => handle.IsNil ? "-" : reader.GetIdentifier( handle );

        var header = reader.Header;

        var lines = new List<string>
        {
            $"Header {reader.SchemaVersion} {header.Product} {header.ProductVersion} {header.Assembly} {header.TargetFramework} {header.CompilationSucceeded}"
        };

        while ( reader.Read() )
        {
            lines.Add(
                reader.NodeKind switch
                {
                    IntrospectionV2NodeKind.AspectClass =>
                        $"AspectClass {Id( reader.AspectClass.AspectClass )} {reader.AspectClass.DisplayName} {reader.AspectClass.Description}",
                    IntrospectionV2NodeKind.AspectLayer => $"AspectLayer {reader.AspectLayer.Name} {reader.AspectLayer.Order}",
                    IntrospectionV2NodeKind.AspectInstance =>
                        $"AspectInstance {reader.AspectInstance.Id} {Id( reader.AspectInstance.TargetDeclaration )} {Describe( reader.AspectInstance.Location )} {reader.AspectInstance.IsSkipped}",
                    IntrospectionV2NodeKind.Predecessor =>
                        $"Predecessor {reader.Predecessor.Kind} {reader.Predecessor.Degree} {Id( reader.Predecessor.Declaration )} {Id( reader.Predecessor.AttributeClass )} {reader.Predecessor.AspectInstance} {Describe( reader.Predecessor.Location )}",
                    IntrospectionV2NodeKind.Transformation =>
                        $"Transformation {reader.Transformation.Kind} {reader.Transformation.AdviceKind} {reader.Transformation.Layer} {reader.Transformation.Order} {Id( reader.Transformation.TargetDeclaration )} {Id( reader.Transformation.IntroducedDeclaration )} {reader.Transformation.Description}",
                    IntrospectionV2NodeKind.Diagnostic =>
                        $"Diagnostic {reader.Diagnostic.Id} {reader.Diagnostic.Severity} {reader.Diagnostic.Message} {Id( reader.Diagnostic.Declaration )} {Describe( reader.Diagnostic.Location )}",
                    _ => reader.NodeKind.ToString()
                } );
        }

        return lines;
    }

    private static string Describe( V2SourceLocation? location ) => location == null ? "-" : $"{location.Value.File}({location.Value.Line},{location.Value.Column})";

    /// <summary>
    /// Reads a file and writes its content again with <see cref="IntrospectionV2Writer"/>, node by node.
    /// </summary>
    public static string RoundTrip( string json )
    {
        using var reader = CreateReader( json );

        return Write(
            writer =>
            {
                var handles = new Dictionary<string, DeclarationHandle>( StringComparer.Ordinal );

                DeclarationHandle Map( DeclarationHandle handle )
                {
                    if ( handle.IsNil )
                    {
                        return default;
                    }

                    var identifier = reader.GetIdentifier( handle );

                    if ( !handles.TryGetValue( identifier, out var mapped ) )
                    {
                        mapped = writer.Declare( identifier );
                        handles.Add( identifier, mapped );
                    }

                    return mapped;
                }

                while ( reader.Read() )
                {
                    switch ( reader.NodeKind )
                    {
                        case IntrospectionV2NodeKind.AspectClass:
                            var aspectClass = reader.AspectClass;
                            writer.WriteStartAspectClass( new V2AspectClassData
                            {
                                AspectClass = Map( aspectClass.AspectClass ), DisplayName = aspectClass.DisplayName, Description = aspectClass.Description
                            } );

                            break;

                        case IntrospectionV2NodeKind.EndAspectClass:
                            writer.WriteEndAspectClass();

                            break;

                        case IntrospectionV2NodeKind.AspectLayer:
                            writer.WriteAspectLayer( reader.AspectLayer );

                            break;

                        case IntrospectionV2NodeKind.AspectInstance:
                            var aspectInstance = reader.AspectInstance;
                            writer.WriteStartAspectInstance( new V2AspectInstanceData
                            {
                                Id = aspectInstance.Id,
                                TargetDeclaration = Map( aspectInstance.TargetDeclaration ),
                                Location = aspectInstance.Location,
                                IsSkipped = aspectInstance.IsSkipped
                            } );

                            break;

                        case IntrospectionV2NodeKind.EndAspectInstance:
                            writer.WriteEndAspectInstance();

                            break;

                        case IntrospectionV2NodeKind.Predecessor:
                            var predecessor = reader.Predecessor;
                            writer.WritePredecessor( new V2PredecessorData
                            {
                                Kind = predecessor.Kind,
                                Degree = predecessor.Degree,
                                Declaration = Map( predecessor.Declaration ),
                                AttributeClass = Map( predecessor.AttributeClass ),
                                AspectInstance = predecessor.AspectInstance,
                                Location = predecessor.Location
                            } );

                            break;

                        case IntrospectionV2NodeKind.Transformation:
                            var transformation = reader.Transformation;
                            writer.WriteTransformation( new V2TransformationData
                            {
                                Kind = transformation.Kind,
                                AdviceKind = transformation.AdviceKind,
                                Layer = transformation.Layer,
                                Order = transformation.Order,
                                TargetDeclaration = Map( transformation.TargetDeclaration ),
                                IntroducedDeclaration = Map( transformation.IntroducedDeclaration ),
                                Description = transformation.Description
                            } );

                            break;

                        case IntrospectionV2NodeKind.Diagnostic:
                            var diagnostic = reader.Diagnostic;
                            writer.WriteDiagnostic( new V2DiagnosticData
                            {
                                Id = diagnostic.Id,
                                Severity = diagnostic.Severity,
                                Message = diagnostic.Message,
                                Declaration = Map( diagnostic.Declaration ),
                                Location = diagnostic.Location
                            } );

                            break;
                    }
                }
            },
            reader.Header );
    }
}
