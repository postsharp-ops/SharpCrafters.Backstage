// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using System.Text.Json;

namespace SharpCrafters.Aspects.Introspection.V2;

/// <summary>
/// The property names of schema version 2, the JSON format of the <c>.mlsym</c> file of Metalama.
/// </summary>
/// <remarks>
/// The names are encoded once. The writer passes them to <see cref="Utf8JsonWriter"/>, and the reader compares them with the
/// UTF-8 bytes of the file, so that neither encodes nor allocates a string for a property name.
/// </remarks>
internal static class V2Names
{
    /// <summary>
    /// The version of the schema that <see cref="IntrospectionV2Writer"/> writes.
    /// </summary>
    public const int SchemaVersion = 2;

    // Root object.
    public static readonly JsonEncodedText SchemaVersionProperty = JsonEncodedText.Encode( "schemaVersion" );
    public static readonly JsonEncodedText Product = JsonEncodedText.Encode( "product" );
    public static readonly JsonEncodedText ProductVersion = JsonEncodedText.Encode( "productVersion" );
    public static readonly JsonEncodedText Assembly = JsonEncodedText.Encode( "assembly" );
    public static readonly JsonEncodedText TargetFramework = JsonEncodedText.Encode( "targetFramework" );
    public static readonly JsonEncodedText CompilationSucceeded = JsonEncodedText.Encode( "compilationSucceeded" );
    public static readonly JsonEncodedText AspectClasses = JsonEncodedText.Encode( "aspectClasses" );

    // Aspect class.
    public static readonly JsonEncodedText Id = JsonEncodedText.Encode( "id" );
    public static readonly JsonEncodedText DisplayName = JsonEncodedText.Encode( "displayName" );
    public static readonly JsonEncodedText Description = JsonEncodedText.Encode( "description" );
    public static readonly JsonEncodedText Layers = JsonEncodedText.Encode( "layers" );
    public static readonly JsonEncodedText AspectInstances = JsonEncodedText.Encode( "aspectInstances" );

    // Aspect layer.
    public static readonly JsonEncodedText Name = JsonEncodedText.Encode( "name" );
    public static readonly JsonEncodedText Order = JsonEncodedText.Encode( "order" );

    // Aspect instance.
    public static readonly JsonEncodedText TargetDeclaration = JsonEncodedText.Encode( "targetDeclaration" );
    public static readonly JsonEncodedText Location = JsonEncodedText.Encode( "location" );
    public static readonly JsonEncodedText IsSkipped = JsonEncodedText.Encode( "isSkipped" );
    public static readonly JsonEncodedText Predecessors = JsonEncodedText.Encode( "predecessors" );
    public static readonly JsonEncodedText Transformations = JsonEncodedText.Encode( "transformations" );
    public static readonly JsonEncodedText Diagnostics = JsonEncodedText.Encode( "diagnostics" );

    // Predecessor.
    public static readonly JsonEncodedText Kind = JsonEncodedText.Encode( "kind" );
    public static readonly JsonEncodedText Degree = JsonEncodedText.Encode( "degree" );
    public static readonly JsonEncodedText Declaration = JsonEncodedText.Encode( "declaration" );
    public static readonly JsonEncodedText AttributeClass = JsonEncodedText.Encode( "attributeClass" );
    public static readonly JsonEncodedText AspectInstance = JsonEncodedText.Encode( "aspectInstance" );

    // Transformation.
    public static readonly JsonEncodedText AdviceKind = JsonEncodedText.Encode( "adviceKind" );
    public static readonly JsonEncodedText Layer = JsonEncodedText.Encode( "layer" );
    public static readonly JsonEncodedText IntroducedDeclaration = JsonEncodedText.Encode( "introducedDeclaration" );

    // Diagnostic.
    public static readonly JsonEncodedText Severity = JsonEncodedText.Encode( "severity" );
    public static readonly JsonEncodedText Message = JsonEncodedText.Encode( "message" );

    // Source location.
    public static readonly JsonEncodedText File = JsonEncodedText.Encode( "file" );
    public static readonly JsonEncodedText Line = JsonEncodedText.Encode( "line" );
    public static readonly JsonEncodedText Column = JsonEncodedText.Encode( "column" );
    public static readonly JsonEncodedText EndLine = JsonEncodedText.Encode( "endLine" );
}
