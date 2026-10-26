// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using System;
using System.Collections.Generic;
using System.Linq;

namespace SharpCrafters.Aspects.Introspection.Query.Querying;

/// <summary>
/// The collections that the <c>introspect</c> commands query, and the fields of their objects
/// (Documentation/future/Introspection-Cli.md of PostSharp, section 5.3). The field names are those of the JSON output; the
/// <c>schema</c> command describes them.
/// </summary>
internal static class IntrospectionSchema
{
    public static IReadOnlyList<TypeDefinition> Types { get; }

    private static readonly Dictionary<string, TypeDefinition> _byName;
    private static readonly Dictionary<string, TypeDefinition> _byCollection;
    private static readonly Dictionary<string, TypeDefinition> _byItem;

    static IntrospectionSchema()
    {
        var predecessorKinds = Enum.GetNames<AspectPredecessorKind>();
        var declarationKinds = Enum.GetNames<IntrospectionDeclarationKind>();
        var mappingKinds = Enum.GetNames<DeclarationMappingKind>();

        Types =
        [
            new TypeDefinition(
                "aspectClass",
                "aspect-classes",
                "aspect-class",
                "The classes of the aspects.",
                [
                    Field( "name", ValueKind.String, "The full name of the class.", ( _, o ) => ((IntrospectionAspectClass) o).FullName, summary: true ),
                    Field(
                        "shortName",
                        ValueKind.String,
                        "The name of the class without its namespace and without the Attribute suffix.",
                        ( _, o ) => ((IntrospectionAspectClass) o).ShortName,
                        summary: true ),
                    Object( "declaration", "declaration", "The type of the class.", ( _, o ) => ((IntrospectionAspectClass) o).Declaration ),
                    Field( "hasLimitedLicense", ValueKind.Boolean, "Whether the licence covers the class only in part.", ( _, o ) => ((IntrospectionAspectClass) o).HasLimitedLicense ),
                    List( "aspectInstances", "aspectInstance", "The aspect instances of the class.", ( _, o ) => ((IntrospectionAspectClass) o).AspectInstances )
                ],
                ["shortName", "name", "aspectInstances/$count"],
                c => c.Model.AspectClasses ),

            new TypeDefinition(
                "aspectInstance",
                "aspect-instances",
                "aspect-instance",
                "The aspect instances: one aspect on one target declaration.",
                [
                    Field( "id", ValueKind.String, "The identifier: 12 hexadecimal characters, of which any unique prefix is accepted.", ( _, o ) => ((IntrospectionAspectInstance) o).Id, summary: true ),
                    Field( "key", ValueKind.String, "The structural key, from which the identifier is computed.", ( _, o ) => ((IntrospectionAspectInstance) o).StructuralKey, summary: true ),
                    Object( "aspectClass", "aspectClass", "The aspect class.", ( _, o ) => ((IntrospectionAspectInstance) o).AspectClass, summary: true ),
                    Object( "targetDeclaration", "declaration", "The declaration to which the aspect is applied.", ( _, o ) => ((IntrospectionAspectInstance) o).TargetDeclaration, summary: true ),
                    Field( "project", ValueKind.String, "The project. A project that compiles the source files of another project has aspect instances of its own on the same source declarations: compare targetDeclaration/id, not the aspect instances, to count source declarations.", ( c, o ) => c.GetProject( o ), summary: true ),
                    Field( "targetFrameworks", ValueKind.StringList, "The target frameworks in which the aspect instance appears.", ( c, o ) => c.GetTargetFrameworks( o ) ),
                    List( "predecessors", "predecessor", "What created the aspect instance.", ( _, o ) => ((IntrospectionAspectInstance) o).Predecessors ),
                    List( "transformations", "transformation", "The transformations, in order.", ( _, o ) => ((IntrospectionAspectInstance) o).Transformations ),
                    Field( "isSkipped", ValueKind.Boolean, "Whether the aspect instance was skipped.", ( _, o ) => ((IntrospectionAspectInstance) o).IsSkipped ),
                    Field( "tags", ValueKind.StringList, "The tags, such as premium for an aspect instance that the licence does not cover.", ( _, o ) => QueryContext.SplitTags( ((IntrospectionAspectInstance) o).Tags ) ),
                    Field( "linesOfCodeAvoided", ValueKind.Integer, "The estimated number of lines of code that the aspect instance saves.", ( _, o ) => ((IntrospectionAspectInstance) o).LinesOfCodeAvoided )
                ],
                ["id", "aspectClass/shortName", "targetDeclaration/id", "transformations/$count"],
                c => c.Model.AspectInstances ),

            new TypeDefinition(
                "predecessor",
                "predecessors",
                "predecessor",
                "What created aspect instances: a custom attribute, a multicast attribute, an XML element, a fabric or an aspect instance.",
                [
                    Field( "id", ValueKind.String, "The identifier: 12 hexadecimal characters, of which any unique prefix is accepted.", ( _, o ) => ((IntrospectionPredecessor) o).Id, summary: true ),
                    Field( "key", ValueKind.String, "The structural key, from which the identifier is computed.", ( _, o ) => ((IntrospectionPredecessor) o).StructuralKey, summary: true ),
                    Field( "kind", ValueKind.Enum, "How the predecessor created its aspect instances: Attribute, a custom attribute on the target declaration; Multicast, a multicast attribute on an enclosing declaration or the assembly; Inherited, an attribute inherited from a base type or member; Xml, an element of a configuration file; ChildAspect, an aspect provider (IAspectProvider), which is the aspect instance of the predecessor; Fabric, RequiredAspect and Interactive, Metalama only.", ( _, o ) => ((IntrospectionPredecessor) o).Kind.ToString(), summary: true, values: predecessorKinds ),
                    Field( "isKindInferred", ValueKind.Boolean, "Whether the kind is inferred, because the file does not give it.", ( _, o ) => ((IntrospectionPredecessor) o).IsKindInferred, summary: true ),
                    Object( "declaration", "declaration", "The declaration that carries the attribute, or the fabric type.", ( _, o ) => ((IntrospectionPredecessor) o).Declaration, summary: true ),
                    Object( "attributeClass", "aspectClass", "The class of the attribute.", ( _, o ) => ((IntrospectionPredecessor) o).AttributeClass ),
                    Field( "location", ValueKind.String, "The location of the predecessor, as path:line:column, when the file gives it.", ( c, o ) => c.GetLocation( (IntrospectionPredecessor) o ), summary: true ),
                    Object( "aspectInstance", "aspectInstance", "The aspect instance that provides the aspect instances of the predecessor, for the kind ChildAspect.", ( _, o ) => ((IntrospectionPredecessor) o).AspectInstance, summary: true ),
                    List( "aspectInstances", "aspectInstance", "The aspect instances that the predecessor created.", ( _, o ) => ((IntrospectionPredecessor) o).AspectInstances ),
                    Field( "project", ValueKind.String, "The first project whose file holds the predecessor.", ( c, o ) => c.GetProject( o ) ),
                    Field( "targetFrameworks", ValueKind.StringList, "The target frameworks in which the predecessor appears.", ( c, o ) => c.GetTargetFrameworks( o ) )
                ],
                ["id", "kind", "declaration/id", "aspectInstances/$count"],
                c => c.Model.Predecessors ),

            new TypeDefinition(
                "transformation",
                "transformations",
                "transformation",
                "The changes that the advices of the aspect instances make to declarations.",
                [
                    Object( "aspectInstance", "aspectInstance", "The aspect instance.", ( _, o ) => ((IntrospectionTransformation) o).AspectInstance, summary: true ),
                    Object( "targetDeclaration", "declaration", "The declaration that the transformation changes.", ( _, o ) => ((IntrospectionTransformation) o).TargetDeclaration, summary: true ),
                    Object( "adviceDeclaration", "declaration", "The advice that makes the transformation.", ( _, o ) => ((IntrospectionTransformation) o).AdviceDeclaration ),
                    Field( "description", ValueKind.String, "A description of the transformation.", ( _, o ) => ((IntrospectionTransformation) o).Description, summary: true ),
                    Field( "semantic", ValueKind.String, "The accessor that the transformation changes, such as Getter.", ( _, o ) => ((IntrospectionTransformation) o).Semantic, summary: true ),
                    Field( "order", ValueKind.Integer, "The order in which the transformation was woven. For the transformations of one declaration, a lower order is outer: it runs first when the declaration is entered and last when it exits. Orders are comparable within one project and target framework only.", ( _, o ) => ((IntrospectionTransformation) o).Order, summary: true ),
                    Field( "linesOfCodeAvoided", ValueKind.Integer, "The estimated number of lines of code that the transformation saves.", ( _, o ) => ((IntrospectionTransformation) o).LinesOfCodeAvoided )
                ],
                ["aspectInstance/aspectClass/shortName", "targetDeclaration/id", "semantic", "description"],
                c => c.Model.Transformations ),

            new TypeDefinition(
                "annotation",
                "annotations",
                "annotation",
                "Information about declarations that is not a transformation, such as the dependencies between properties.",
                [
                    Object( "targetDeclaration", "declaration", "The annotated declaration.", ( _, o ) => ((IntrospectionAnnotation) o).TargetDeclaration, summary: true ),
                    Object( "annotationClass", "declaration", "The class of the annotation.", ( _, o ) => ((IntrospectionAnnotation) o).AnnotationClass ),
                    Field( "arguments", ValueKind.String, "The arguments of the annotation.", ( _, o ) => ((IntrospectionAnnotation) o).Arguments ),
                    Field( "description", ValueKind.String, "A description of the annotation.", ( _, o ) => ((IntrospectionAnnotation) o).Description, summary: true ),
                    Field( "linesOfCodeAvoided", ValueKind.Integer, "The estimated number of lines of code that the annotation stands for.", ( _, o ) => ((IntrospectionAnnotation) o).LinesOfCodeAvoided )
                ],
                ["targetDeclaration/id", "description"],
                c => c.Model.Annotations ),

            new TypeDefinition(
                "declarationMapping",
                "declaration-mappings",
                "declaration-mapping",
                "The replacements of source declarations by woven ones, such as a field encapsulated as a property.",
                [
                    Object( "source", "declaration", "The source declaration.", ( _, o ) => ((IntrospectionDeclarationMapping) o).Source, summary: true ),
                    Object( "target", "declaration", "The woven declaration.", ( _, o ) => ((IntrospectionDeclarationMapping) o).Target, summary: true ),
                    Field( "kind", ValueKind.Enum, "The kind of the mapping.", ( _, o ) => ((IntrospectionDeclarationMapping) o).Kind?.ToString(), summary: true, values: mappingKinds )
                ],
                ["source/id", "target/id", "kind"],
                c => c.Model.DeclarationMappings ),

            new TypeDefinition(
                "declaration",
                "declarations",
                "declaration",
                "The declarations that the file mentions, with what applies to them.",
                [
                    Field( "id", ValueKind.String, "The documentation comment identifier.", ( _, o ) => ((IntrospectionDeclaration) o).Id, summary: true ),
                    Field( "qualifiedId", ValueKind.String, "The documentation comment identifier qualified with the assembly.", ( _, o ) => ((IntrospectionDeclaration) o).QualifiedId ),
                    Field( "kind", ValueKind.Enum, "The kind of the declaration.", ( _, o ) => ((IntrospectionDeclaration) o).Kind.ToString(), summary: true, values: declarationKinds ),
                    Field( "name", ValueKind.String, "The name, without the declaring type and the namespace.", ( _, o ) => ((IntrospectionDeclaration) o).Name ),
                    Field( "namespace", ValueKind.String, "The namespace.", ( _, o ) => ((IntrospectionDeclaration) o).Namespace ),
                    Field( "assembly", ValueKind.String, "The assembly that declares the declaration.", ( _, o ) => ((IntrospectionDeclaration) o).Assembly ),
                    Field( "project", ValueKind.String, "The project that builds the assembly of the declaration, or the first project whose file mentions it.", ( c, o ) => c.GetProject( o ) ),
                    Field( "targetFrameworks", ValueKind.StringList, "The target frameworks whose files mention the declaration.", ( c, o ) => c.GetTargetFrameworks( o ) ),
                    Field( "location", ValueKind.String, "The location of the declaration, as path:line:column, when the product records it.", ( c, o ) => c.GetLocation( (IntrospectionDeclaration) o ) ),
                    Object( "declaringType", "declaration", "The type that declares a member or a nested type.", ( _, o ) => ((IntrospectionDeclaration) o).DeclaringType ),
                    Object( "containingDeclaration", "declaration", "The method of a parameter, the property or event of an accessor, the property of a backing field.", ( _, o ) => ((IntrospectionDeclaration) o).ContainingDeclaration ),
                    List( "accessors", "declaration", "The accessors of a property or an event.", ( _, o ) => ((IntrospectionDeclaration) o).Accessors ),
                    Object( "backingField", "declaration", "The backing field of a property.", ( _, o ) => ((IntrospectionDeclaration) o).BackingField ),
                    List( "members", "declaration", "The members of a type that the file mentions.", ( _, o ) => ((IntrospectionDeclaration) o).Members ),
                    List( "aspectInstances", "aspectInstance", "The aspect instances applied to the declaration.", ( c, o ) => c.GetAspectInstances( (IntrospectionDeclaration) o ) ),
                    List( "transformations", "transformation", "The transformations of the declaration.", ( c, o ) => c.GetTransformations( (IntrospectionDeclaration) o ) ),
                    List( "annotations", "annotation", "The annotations of the declaration.", ( c, o ) => c.GetAnnotations( (IntrospectionDeclaration) o ) ),
                    List( "declarationMappings", "declarationMapping", "The declaration mappings whose source or target is the declaration.", ( c, o ) => c.GetDeclarationMappings( (IntrospectionDeclaration) o ) )
                ],
                ["id", "kind", "aspectInstances/$count", "transformations/$count"],
                c => c.Model.Declarations ),

            new TypeDefinition(
                "diagnostic",
                "diagnostics",
                "diagnostic",
                "The messages that aspects and the framework reported.",
                [
                    Field( "id", ValueKind.String, "The identifier of the diagnostic.", ( _, _ ) => null, summary: true ),
                    Field( "severity", ValueKind.String, "The severity.", ( _, _ ) => null, summary: true ),
                    Field( "message", ValueKind.String, "The message.", ( _, _ ) => null, summary: true ),
                    Object( "aspectInstance", "aspectInstance", "The aspect instance that reported the diagnostic.", ( _, _ ) => null )
                ],
                ["id", "severity", "message"],
                _ => [] )
        ];

        _byName = Types.ToDictionary( t => t.Name, StringComparer.Ordinal );
        _byCollection = Types.ToDictionary( t => t.CollectionName, StringComparer.OrdinalIgnoreCase );
        _byItem = Types.ToDictionary( t => t.ItemName, StringComparer.OrdinalIgnoreCase );
    }

    public static TypeDefinition GetType( string name ) => _byName[name];

    public static TypeDefinition? FindCollection( string collectionName ) => _byCollection.GetValueOrDefault( collectionName );

    public static TypeDefinition? FindItem( string itemName ) => _byItem.GetValueOrDefault( itemName );

    public static string CollectionNames => string.Join( ", ", Types.Select( t => t.CollectionName ) );

    public static string ItemNames => string.Join( ", ", Types.Select( t => t.ItemName ) );

    private static FieldDefinition Field(
        string name,
        ValueKind kind,
        string description,
        Func<QueryContext, object, object?> getValue,
        bool summary = false,
        IReadOnlyList<string>? values = null )
        => new( name, kind, description, getValue ) { IsInSummary = summary, EnumValues = values };

    private static FieldDefinition Object( string name, string typeName, string description, Func<QueryContext, object, object?> getValue, bool summary = false )
        => new( name, ValueKind.Object, description, getValue ) { TypeName = typeName, IsInSummary = summary };

    private static FieldDefinition List( string name, string typeName, string description, Func<QueryContext, object, object?> getValue )
        => new( name, ValueKind.ObjectList, description, getValue ) { TypeName = typeName };
}
