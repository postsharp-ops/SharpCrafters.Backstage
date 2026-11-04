// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using System.Collections.Generic;

namespace SharpCrafters.Aspects.Introspection.Query;

/// <summary>
/// The common model of the introspection of aspects for one file, which the commands of PostSharp and Metalama query
/// (Documentation/future/Introspection-Cli.md of PostSharp, section 6). It is built once by a loader, such as
/// <see cref="IntrospectionV1Loader"/>, and is not changed after.
/// </summary>
public sealed class IntrospectionModel
{
    internal IntrospectionModel( string assembly )
    {
        this.Assembly = assembly;
    }

    /// <summary>
    /// Gets the name of the assembly that the file describes.
    /// </summary>
    public string Assembly { get; internal set; }

    /// <summary>
    /// Gets the schema version of the file.
    /// </summary>
    public int SchemaVersion { get; internal set; }

    /// <summary>
    /// Gets the version of the product that wrote the file, or <c>null</c>.
    /// </summary>
    public string? ProductVersion { get; internal set; }

    /// <summary>
    /// Gets the number of lines of code of the assembly.
    /// </summary>
    public int LinesOfCode { get; internal set; }

    /// <summary>
    /// Gets the number of types that an aspect enhances.
    /// </summary>
    public int EnhancedTypesCount { get; internal set; }

    /// <summary>
    /// Gets the aspect classes, in the order of the file.
    /// </summary>
    public IReadOnlyList<IntrospectionAspectClass> AspectClasses => this.AspectClassesList;

    /// <summary>
    /// Gets the aspect instances, in the order of the file.
    /// </summary>
    public IReadOnlyList<IntrospectionAspectInstance> AspectInstances => this.AspectInstancesList;

    /// <summary>
    /// Gets the predecessors, in the order of the file.
    /// </summary>
    public IReadOnlyList<IntrospectionPredecessor> Predecessors => this.PredecessorsList;

    /// <summary>
    /// Gets the transformations of all the aspect instances, in the order of the file.
    /// </summary>
    public IReadOnlyList<IntrospectionTransformation> Transformations => this.TransformationsList;

    /// <summary>
    /// Gets the annotations, in the order of the file.
    /// </summary>
    public IReadOnlyList<IntrospectionAnnotation> Annotations => this.AnnotationsList;

    /// <summary>
    /// Gets the declaration mappings, in the order of the file.
    /// </summary>
    public IReadOnlyList<IntrospectionDeclarationMapping> DeclarationMappings => this.DeclarationMappingsList;

    /// <summary>
    /// Gets the declarations that the file mentions, in the order of their first mention, and the declaring types and methods of
    /// these declarations.
    /// </summary>
    public IReadOnlyList<IntrospectionDeclaration> Declarations => this.DeclarationsList;

    /// <summary>
    /// Gets the declaration whose qualified identifier (<see cref="IntrospectionDeclaration.QualifiedId"/>) is given, or <c>null</c>.
    /// </summary>
    public IntrospectionDeclaration? GetDeclaration( string qualifiedId ) => this.DeclarationsByQualifiedId.GetValueOrDefault( qualifiedId );

    internal List<IntrospectionAspectClass> AspectClassesList { get; } = [];

    internal List<IntrospectionAspectInstance> AspectInstancesList { get; } = [];

    internal List<IntrospectionPredecessor> PredecessorsList { get; } = [];

    internal List<IntrospectionTransformation> TransformationsList { get; } = [];

    internal List<IntrospectionAnnotation> AnnotationsList { get; } = [];

    internal List<IntrospectionDeclarationMapping> DeclarationMappingsList { get; } = [];

    internal List<IntrospectionDeclaration> DeclarationsList { get; } = [];

    internal Dictionary<string, IntrospectionDeclaration> DeclarationsByQualifiedId { get; } = new( System.StringComparer.Ordinal );
}
