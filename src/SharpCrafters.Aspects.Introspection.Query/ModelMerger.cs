// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Aspects.Introspection.Query.Projects;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SharpCrafters.Aspects.Introspection.Query;

/// <summary>
/// Merges the models of the files of several projects and target frameworks into one model
/// (Documentation/future/Introspection-Cli.md of PostSharp, section 5.6). The objects are matched by keys that do not depend on
/// the build: declarations by their identifier qualified with their assembly, aspect classes by their type, aspect instances
/// and predecessors by their structural keys, and transformations, annotations and declaration mappings by their content.
/// </summary>
/// <remarks>
/// A file refers to declarations of other projects, such as the aspect class of a library or the base class of an inherited
/// attribute, so the declarations of all the files are one set. A project has one file per target framework, with almost the
/// same content: an object is kept once, with the targets where it appears. The values of an object that differ between its
/// files, such as the order of a transformation, are those of the first file.
/// </remarks>
internal static class ModelMerger
{
    public static (IntrospectionModel Model, ModelProvenance Provenance) Merge( IReadOnlyList<(IntrospectionTarget Target, IntrospectionModel Model)> files )
    {
        var provenance = new ModelProvenance( files.Select( f => f.Target ).ToList() );
        var first = files[0].Model;

        var merged = new IntrospectionModel( files.Select( f => f.Model.Assembly ).Distinct( StringComparer.Ordinal ).Count() == 1 ? first.Assembly : "" )
        {
            SchemaVersion = first.SchemaVersion,
            ProductVersion = first.ProductVersion,
            LinesOfCode = files.Sum( f => f.Model.LinesOfCode ),
            EnhancedTypesCount = files.Sum( f => f.Model.EnhancedTypesCount )
        };

        var declarations = new Dictionary<string, IntrospectionDeclaration>( StringComparer.Ordinal );
        var aspectClasses = new Dictionary<string, IntrospectionAspectClass>( StringComparer.Ordinal );
        var aspectInstances = new Dictionary<string, IntrospectionAspectInstance>( StringComparer.Ordinal );
        var predecessors = new Dictionary<string, IntrospectionPredecessor>( StringComparer.Ordinal );
        var transformations = new HashSet<(string, string, string?, string?, string?)>();
        var annotations = new HashSet<(string, string?, string?, string?)>();
        var mappings = new HashSet<(string, string)>();

        // The declarations first, then their relations, which may point to declarations of a later file.
        foreach ( var (target, model) in files )
        {
            foreach ( var declaration in model.Declarations )
            {
                if ( !declarations.TryGetValue( declaration.QualifiedId, out var canonical ) )
                {
                    canonical = new IntrospectionDeclaration( declaration.Assembly, declaration.Id, declaration.Kind, declaration.Name, declaration.SourceIdentifier )
                    {
                        Namespace = declaration.Namespace
                    };

                    declarations.Add( declaration.QualifiedId, canonical );
                    merged.DeclarationsList.Add( canonical );
                    merged.DeclarationsByQualifiedId.Add( declaration.QualifiedId, canonical );
                }

                provenance.Add( canonical, target );
            }
        }

        IntrospectionDeclaration Map( IntrospectionDeclaration declaration ) => declarations[declaration.QualifiedId];

        IntrospectionDeclaration? MapOptional( IntrospectionDeclaration? declaration ) => declaration == null ? null : Map( declaration );

        foreach ( var (_, model) in files )
        {
            foreach ( var declaration in model.Declarations )
            {
                var canonical = Map( declaration );
                canonical.DeclaringType ??= MapOptional( declaration.DeclaringType );
                canonical.ContainingDeclaration ??= MapOptional( declaration.ContainingDeclaration );
                canonical.BackingField ??= MapOptional( declaration.BackingField );
                AddDistinct( canonical.MembersList, declaration.Members.Select( Map ) );
                AddDistinct( canonical.AccessorsList, declaration.Accessors.Select( Map ) );
            }
        }

        IntrospectionAspectClass MapClass( IntrospectionAspectClass aspectClass )
        {
            var key = aspectClass.Declaration.QualifiedId;

            if ( !aspectClasses.TryGetValue( key, out var canonical ) )
            {
                canonical = new IntrospectionAspectClass( Map( aspectClass.Declaration ) );
                aspectClasses.Add( key, canonical );
                merged.AspectClassesList.Add( canonical );
            }

            canonical.HasLimitedLicense |= aspectClass.HasLimitedLicense;

            return canonical;
        }

        // Every aspect class of every file, with its license flag, including those of which no aspect instance or predecessor
        // of the file is new.
        foreach ( var (target, model) in files )
        {
            foreach ( var aspectClass in model.AspectClasses )
            {
                provenance.Add( MapClass( aspectClass ), target );
            }
        }

        // The aspect instances, before the predecessors that refer to them.
        foreach ( var (target, model) in files )
        {
            foreach ( var instance in model.AspectInstances )
            {
                if ( !aspectInstances.TryGetValue( instance.StructuralKey, out var canonical ) )
                {
                    canonical = new IntrospectionAspectInstance( MapClass( instance.AspectClass ), Map( instance.TargetDeclaration ) )
                    {
                        Id = instance.Id,
                        StructuralKey = instance.StructuralKey,
                        LinesOfCodeAvoided = instance.LinesOfCodeAvoided,
                        Tags = instance.Tags,
                        IsSkipped = instance.IsSkipped
                    };

                    aspectInstances.Add( instance.StructuralKey, canonical );
                    merged.AspectInstancesList.Add( canonical );
                    canonical.AspectClass.AspectInstancesList.Add( canonical );
                }

                provenance.Add( canonical, target );

                foreach ( var transformation in instance.Transformations )
                {
                    var key = (instance.StructuralKey, transformation.TargetDeclaration.QualifiedId, transformation.AdviceDeclaration?.QualifiedId, transformation.Semantic,
                               transformation.Description);

                    if ( transformations.Add( key ) )
                    {
                        var copy = new IntrospectionTransformation( canonical, Map( transformation.TargetDeclaration ) )
                        {
                            AdviceDeclaration = MapOptional( transformation.AdviceDeclaration ),
                            Description = transformation.Description,
                            Semantic = transformation.Semantic,
                            Order = transformation.Order,
                            LinesOfCodeAvoided = transformation.LinesOfCodeAvoided
                        };

                        canonical.TransformationsList.Add( copy );
                        merged.TransformationsList.Add( copy );
                        provenance.Add( copy, target );
                    }
                    else
                    {
                        provenance.Add(
                            canonical.TransformationsList.First(
                                t => t.TargetDeclaration.QualifiedId == key.Item2 && t.AdviceDeclaration?.QualifiedId == key.Item3 && t.Semantic == key.Semantic
                                     && t.Description == key.Description ),
                            target );
                    }
                }
            }
        }

        foreach ( var (target, model) in files )
        {
            foreach ( var predecessor in model.Predecessors )
            {
                if ( !predecessors.TryGetValue( predecessor.StructuralKey, out var canonical ) )
                {
                    canonical = new IntrospectionPredecessor( predecessor.Kind, predecessor.IsKindInferred )
                    {
                        Id = predecessor.Id,
                        StructuralKey = predecessor.StructuralKey,
                        Declaration = MapOptional( predecessor.Declaration ),
                        AttributeClass = predecessor.AttributeClass == null ? null : MapClass( predecessor.AttributeClass ),
                        File = predecessor.File,
                        KeyFile = predecessor.KeyFile,
                        Line = predecessor.Line,
                        Column = predecessor.Column,
                        AspectInstance = predecessor.AspectInstance == null ? null : aspectInstances[predecessor.AspectInstance.StructuralKey]
                    };

                    predecessors.Add( predecessor.StructuralKey, canonical );
                    merged.PredecessorsList.Add( canonical );
                }

                provenance.Add( canonical, target );

                foreach ( var instance in predecessor.AspectInstances )
                {
                    var canonicalInstance = aspectInstances[instance.StructuralKey];

                    if ( !canonical.AspectInstancesList.Contains( canonicalInstance ) )
                    {
                        canonical.AspectInstancesList.Add( canonicalInstance );
                        canonicalInstance.PredecessorsList.Add( canonical );
                    }
                }
            }

            foreach ( var annotation in model.Annotations )
            {
                if ( annotations.Add( (annotation.TargetDeclaration.QualifiedId, annotation.AnnotationClass?.QualifiedId, annotation.Arguments, annotation.Description) ) )
                {
                    var copy = new IntrospectionAnnotation( Map( annotation.TargetDeclaration ) )
                    {
                        AnnotationClass = MapOptional( annotation.AnnotationClass ),
                        Arguments = annotation.Arguments,
                        Description = annotation.Description,
                        LinesOfCodeAvoided = annotation.LinesOfCodeAvoided
                    };

                    merged.AnnotationsList.Add( copy );
                    provenance.Add( copy, target );
                }
            }

            foreach ( var mapping in model.DeclarationMappings )
            {
                if ( mappings.Add( (mapping.Source.QualifiedId, mapping.Target.QualifiedId) ) )
                {
                    var copy = new IntrospectionDeclarationMapping( Map( mapping.Source ), Map( mapping.Target ), mapping.Kind );
                    merged.DeclarationMappingsList.Add( copy );
                    provenance.Add( copy, target );
                }
            }
        }

        // An aspect class is where its aspect instances are.
        foreach ( var aspectClass in merged.AspectClassesList )
        {
            foreach ( var instance in aspectClass.AspectInstances )
            {
                foreach ( var target in provenance.GetTargets( instance ) )
                {
                    provenance.Add( aspectClass, target );
                }
            }
        }

        return (merged, provenance);

        static void AddDistinct( List<IntrospectionDeclaration> list, IEnumerable<IntrospectionDeclaration> items )
        {
            foreach ( var item in items )
            {
                if ( !list.Contains( item ) )
                {
                    list.Add( item );
                }
            }
        }
    }

    /// <summary>
    /// Gets the provenance of the model of one file: every object comes from its target.
    /// </summary>
    public static ModelProvenance Single( IntrospectionTarget target ) => new( [target] );
}
