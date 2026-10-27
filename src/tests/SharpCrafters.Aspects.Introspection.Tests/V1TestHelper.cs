// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace SharpCrafters.Aspects.Introspection.Tests;

internal static class V1TestHelper
{
    public static string FixturesDirectory => Path.Combine( AppContext.BaseDirectory, "Fixtures" );

    // Writes with the settings of WriteWeavingSymbolsTask of PostSharp: UTF-8 with a byte order mark, indented by two spaces.
    public static string Write( Action<IntrospectionV1Writer> write, SymbolsData symbols = default )
    {
        var stream = new MemoryStream();

        using ( var xmlWriter = XmlWriter.Create( stream, new XmlWriterSettings { Indent = true, Encoding = new UTF8Encoding( true ) } ) )
        {
            using var writer = new IntrospectionV1Writer( xmlWriter, symbols );
            write( writer );
        }

        return Normalize( Encoding.UTF8.GetString( stream.ToArray() ) );
    }

    // A declaration of the tests: each call creates a key of its own, and so a handle of its own.
    public static DeclarationHandle Declare( this IntrospectionV1Writer writer, string identifier )
        => writer.GetDeclarationHandle( new TestDeclaration( identifier ), _formatTestDeclaration );

    private static readonly Func<TestDeclaration, string> _formatTestDeclaration = d => d.Identifier;

    private sealed class TestDeclaration( string identifier )
    {
        public string Identifier { get; } = identifier;
    }

    public static IntrospectionV1Reader CreateReader( string xml ) => IntrospectionV1Reader.Create( new MemoryStream( Encoding.UTF8.GetBytes( xml ) ) );

    // The line endings, which depend on the checkout and on the writer, are compared as LF.
    public static string ToLf( this string text ) => Regex.Replace( text, "\r\n", "\n" );

    public static string Normalize( string text ) => text.TrimStart( '﻿' ).ToLf().TrimEnd();

    /// <summary>
    /// Describes the content of a file element by element, with the effective values and the identifiers of the declarations:
    /// two files with the same description have the same content, whatever their name table and their implied values.
    /// </summary>
    public static List<string> Describe( string xml )
    {
        using var reader = CreateReader( xml );

        string Id( DeclarationHandle handle ) => handle.IsNil ? "-" : reader.GetIdentifier( handle );

        var symbols = reader.Symbols;

        var lines = new List<string>
        {
            $"Symbols {reader.SchemaVersion} {symbols.ProductVersion} {symbols.LinesOfCode} {symbols.ProductLinesOfCode} {symbols.EnhancedTypesCount}"
        };

        while ( reader.Read() )
        {
            lines.Add(
                reader.NodeKind switch
                {
                    IntrospectionNodeKind.DeclarationMapping => $"Mapping {Id( reader.DeclarationMapping.Source )} {Id( reader.DeclarationMapping.Target )} {reader.DeclarationMapping.Kind}",
                    IntrospectionNodeKind.AspectClass => $"Class {Id( reader.AspectClass.AspectClass )} {reader.AspectClass.HasLimitedLicense}",
                    IntrospectionNodeKind.Predecessor => Describe( reader.Predecessor, Id ),
                    IntrospectionNodeKind.AspectInstance => $"AspectInstance {Id( reader.AspectInstance.TargetDeclaration )} {reader.AspectInstance.LinesOfCodeAvoided} {reader.AspectInstance.Tags} {reader.AspectInstance.Id}",
                    IntrospectionNodeKind.Transformation => Describe( reader.Transformation, Id ),
                    IntrospectionNodeKind.Annotation => $"Annotation {Id( reader.Annotation.TargetDeclaration )} {Id( reader.Annotation.AnnotationClass )} {reader.Annotation.Arguments} {reader.Annotation.Description} {reader.Annotation.LinesOfCodeAvoided}",
                    _ => reader.NodeKind.ToString()
                } );
        }

        return lines;
    }

    private static string Describe( PredecessorData p, Func<DeclarationHandle, string> id )
        => $"Predecessor {id( p.Declaration )} {p.File} {p.Line} {p.Column} {p.IsForwardReference} {p.Description} {p.Id} {p.Kind} {p.ProvidedBy}";

    private static string Describe( TransformationData t, Func<DeclarationHandle, string> id )
        => $"Transformation {id( t.TargetDeclaration )} {id( t.AdviceDeclaration )} {t.Description} {t.Semantic} {t.Order} {t.LinesOfCodeAvoided}";

    /// <summary>
    /// Reads a file and writes its content again, through the data structures only, as a product would write it.
    /// </summary>
    public static string RoundTrip( string xml )
    {
        using var reader = CreateReader( xml );

        return Write(
            writer =>
            {
                var handles = new Dictionary<DeclarationHandle, DeclarationHandle>();

                DeclarationHandle Map( DeclarationHandle handle )
                {
                    if ( handle.IsNil )
                    {
                        return default;
                    }

                    if ( !handles.TryGetValue( handle, out var mapped ) )
                    {
                        mapped = writer.Declare( reader.GetIdentifier( handle ) );
                        handles.Add( handle, mapped );
                    }

                    return mapped;
                }

                while ( reader.Read() )
                {
                    switch ( reader.NodeKind )
                    {
                        case IntrospectionNodeKind.DeclarationMapping:
                            var mapping = reader.DeclarationMapping;
                            writer.WriteDeclarationMapping( mapping with { Source = Map( mapping.Source ), Target = Map( mapping.Target ) } );

                            break;

                        case IntrospectionNodeKind.AspectClass:
                            var aspectClass = reader.AspectClass;
                            writer.WriteStartAspectClass( aspectClass with { AspectClass = Map( aspectClass.AspectClass ) } );

                            break;

                        case IntrospectionNodeKind.EndAspectClass:
                            writer.WriteEndAspectClass();

                            break;

                        case IntrospectionNodeKind.Predecessor:
                            var predecessor = reader.Predecessor;
                            writer.WriteStartPredecessor( predecessor with { Declaration = Map( predecessor.Declaration ) } );

                            break;

                        case IntrospectionNodeKind.EndPredecessor:
                            writer.WriteEndPredecessor();

                            break;

                        case IntrospectionNodeKind.AspectInstance:
                            var aspectInstance = reader.AspectInstance;
                            writer.WriteStartAspectInstance( aspectInstance with { TargetDeclaration = Map( aspectInstance.TargetDeclaration ) } );

                            break;

                        case IntrospectionNodeKind.EndAspectInstance:
                            writer.WriteEndAspectInstance();

                            break;

                        case IntrospectionNodeKind.Transformation:
                            var transformation = reader.Transformation;

                            writer.WriteTransformation(
                                transformation with
                                {
                                    TargetDeclaration = Map( transformation.TargetDeclaration ),
                                    AdviceDeclaration = Map( transformation.AdviceDeclaration )
                                } );

                            break;

                        case IntrospectionNodeKind.Annotation:
                            var annotation = reader.Annotation;

                            writer.WriteAnnotation(
                                annotation with { TargetDeclaration = Map( annotation.TargetDeclaration ), AnnotationClass = Map( annotation.AnnotationClass ) } );

                            break;
                    }
                }
            },
            reader.Symbols );
    }
}
