// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Aspects.Introspection.Query.V1;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SharpCrafters.Aspects.Introspection.Query;

/// <summary>
/// Loads a weaving symbol file of version 1, the <c>.pssym</c> file of PostSharp, into the common model
/// (Documentation/future/Introspection-Cli.md of PostSharp, section 6.1).
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>Each <c>Target</c> element becomes an aspect instance, and the enclosing <c>Instance</c> element its predecessor,
/// shared by all the aspect instances of the element.</item>
/// <item>The kind of a predecessor is the <c>Origin</c> of the file when it gives one. Otherwise it is inferred: an XML element
/// when the predecessor has a file and no declaration, an inherited attribute when it is a reference to an attribute of
/// another assembly (<c>ForwardRef</c>), an attribute when every aspect instance of the predecessor is applied to the
/// declaration of the attribute, and a multicast attribute otherwise. An attribute that is inherited within its assembly is
/// therefore inferred as a multicast attribute.</item>
/// <item>A transformation whose advice is not of the aspect class belongs to an aspect instance that an aspect provider created,
/// when the file does not write it as its own aspect instance: such an aspect instance is created, of the class of the advice,
/// on the target declaration of the transformation, with a predecessor of kind <see cref="AspectPredecessorKind.ChildAspect"/>
/// that refers to the aspect instance of the aspect provider.</item>
/// <item>A predecessor of kind <c>ChildAspect</c> in the file refers through <c>ProvidedBy</c> to the <c>Id</c> of the aspect
/// instance that provides its aspect instances.</item>
/// <item>The reflection names are converted to documentation comment identifiers qualified with their assembly.</item>
/// <item>The declaration mappings are applied to the declarations of the predecessors, the aspect instances, the
/// transformations and the annotations: a declaration that is the target of a mapping is replaced by the first source of the
/// chain of mappings, such as a field for the property that encapsulates it.</item>
/// <item>A property is linked to its accessors and its backing field, an event to its accessors, a parameter to its method,
/// and a member to its declaring type, when the file mentions them.</item>
/// </list>
/// </remarks>
public static class IntrospectionV1Loader
{
    /// <summary>
    /// Loads a file from a stream.
    /// </summary>
    /// <param name="stream">The file.</param>
    /// <param name="assembly">The name of the assembly that the file describes, or <c>null</c> to infer it from the declarations
    /// of the aspect instances.</param>
    /// <param name="projectDirectory">The directory of the project, or <c>null</c>. The structural key of an XML predecessor holds
    /// the path of its file relative to this directory, so that its identifier is the same on every machine; without it, the
    /// path is the one of the file, which PostSharp writes in full.</param>
    public static IntrospectionModel Load( Stream stream, string? assembly = null, string? projectDirectory = null )
    {
        using var reader = IntrospectionV1Reader.Create( stream );

        return Load( reader, assembly, projectDirectory );
    }

    /// <summary>
    /// Loads a file from a reader positioned at its start.
    /// </summary>
    /// <param name="reader">The reader.</param>
    /// <param name="assembly">The name of the assembly that the file describes, or <c>null</c> to infer it from the declarations
    /// of the aspect instances.</param>
    /// <param name="projectDirectory">The directory of the project, or <c>null</c>; see
    /// <see cref="Load(Stream, string?, string?)"/>.</param>
    public static IntrospectionModel Load( IntrospectionV1Reader reader, string? assembly = null, string? projectDirectory = null )
        => new Loader( reader, projectDirectory ).Load( assembly );

    private sealed class Loader
    {
        private readonly IntrospectionV1Reader _reader;
        private readonly string? _projectDirectory;
        private readonly Dictionary<string, IntrospectionDeclaration> _declarations = new( StringComparer.Ordinal );
        private readonly Dictionary<string, IntrospectionAspectClass> _aspectClasses = new( StringComparer.Ordinal );

        // The source of each target of a declaration mapping, by reflection name.
        private readonly Dictionary<string, string> _mappingSources = new( StringComparer.Ordinal );

        // The predecessors of kind ChildAspect that the file writes, with the Id of the aspect instance that provides them.
        private readonly List<(IntrospectionPredecessor Predecessor, string ProvidedBy)> _providedPredecessors = [];

        private IntrospectionModel _model = null!;

        public Loader( IntrospectionV1Reader reader, string? projectDirectory )
        {
            this._reader = reader;
            this._projectDirectory = projectDirectory;
        }

        public IntrospectionModel Load( string? assembly )
        {
            // The name of the assembly, when it is not given, is inferred after the file is read.
            this._model = new IntrospectionModel( assembly ?? "" )
            {
                SchemaVersion = this._reader.SchemaVersion,
                ProductVersion = this._reader.Symbols.ProductVersion,
                LinesOfCode = this._reader.Symbols.LinesOfCode,
                EnhancedTypesCount = this._reader.Symbols.EnhancedTypesCount
            };

            IntrospectionAspectClass? aspectClass = null;
            PendingPredecessor? predecessor = null;
            PendingAspectInstance? aspectInstance = null;

            while ( this._reader.Read() )
            {
                switch ( this._reader.NodeKind )
                {
                    case IntrospectionNodeKind.DeclarationMapping:
                        this.ReadDeclarationMapping( this._reader.DeclarationMapping );

                        break;

                    case IntrospectionNodeKind.AspectClass:
                        aspectClass = this.GetAspectClass( this._reader.GetIdentifier( this._reader.AspectClass.AspectClass ) );
                        aspectClass.HasLimitedLicense |= this._reader.AspectClass.HasLimitedLicense;

                        break;

                    case IntrospectionNodeKind.EndAspectClass:
                        aspectClass = null;

                        break;

                    case IntrospectionNodeKind.Predecessor:
                        predecessor = new PendingPredecessor( this._reader.Predecessor, this.GetMappedDeclaration( this._reader.Predecessor.Declaration ) );

                        break;

                    case IntrospectionNodeKind.EndPredecessor:
                        this.CompletePredecessor( predecessor!, aspectClass! );
                        predecessor = null;

                        break;

                    case IntrospectionNodeKind.AspectInstance:
                        var data = this._reader.AspectInstance;

                        var instance = new IntrospectionAspectInstance( aspectClass!, this.GetMappedDeclaration( data.TargetDeclaration )! )
                        {
                            LinesOfCodeAvoided = data.LinesOfCodeAvoided, Tags = data.Tags, FileId = data.Id
                        };

                        aspectInstance = new PendingAspectInstance( instance );
                        predecessor!.AspectInstances.Add( aspectInstance );

                        break;

                    case IntrospectionNodeKind.Transformation:
                        this.ReadTransformation( this._reader.Transformation, aspectInstance! );

                        break;

                    case IntrospectionNodeKind.EndAspectInstance:
                        aspectInstance = null;

                        break;

                    case IntrospectionNodeKind.Annotation:
                        this.ReadAnnotation( this._reader.Annotation );

                        break;
                }
            }

            this.ResolveProvidedPredecessors();
            this.LinkMembers();
            if ( assembly == null )
            {
                this._model.Assembly = InferAssembly( this._model );
            }

            // The key of an XML predecessor holds the assembly.
            StructuralKeys.Assign( this._model );

            return this._model;
        }

        private void ReadDeclarationMapping( DeclarationMappingData data )
        {
            var source = this._reader.GetIdentifier( data.Source );
            var target = this._reader.GetIdentifier( data.Target );

            // The first mapping of a target is the one that counts; a cycle cannot form, because a mapping is applied to the
            // names read after it, and the targets of the mappings are resolved when they are applied.
            if ( !this._mappingSources.ContainsKey( target ) )
            {
                this._mappingSources.Add( target, source );
            }

            this._model.DeclarationMappingsList.Add(
                new IntrospectionDeclarationMapping( this.GetDeclaration( source ), this.GetDeclaration( target ), data.Kind ) );
        }

        private void ReadTransformation( TransformationData data, PendingAspectInstance aspectInstance )
        {
            var target = this.GetMappedDeclaration( data.TargetDeclaration )!;
            var advice = this.GetDeclaration( data.AdviceDeclaration );
            var instance = aspectInstance.Instance;

            // The advice of an aspect instance that an aspect provider created is of another class than the aspect provider.
            // The aspect class is compared by identifier: the file may name it through another object of the compiler.
            var adviceClass = advice == null ? null : GetAdviceClass( advice );

            if ( adviceClass != null && adviceClass.QualifiedId != instance.AspectClass.Declaration.QualifiedId )
            {
                var key = (adviceClass.QualifiedId, target.QualifiedId);

                if ( !aspectInstance.Children.TryGetValue( key, out var child ) )
                {
                    child = new IntrospectionAspectInstance( this.GetAspectClass( adviceClass ), target );
                    aspectInstance.Children.Add( key, child );
                    aspectInstance.ChildOrder.Add( child );
                }

                instance = child;
            }

            var transformation = new IntrospectionTransformation( instance, target )
            {
                AdviceDeclaration = advice,
                Description = data.Description,
                Semantic = data.Semantic,
                Order = data.Order,
                LinesOfCodeAvoided = data.LinesOfCodeAvoided
            };

            instance.TransformationsList.Add( transformation );

            // The transformations of the model keep the order of the file, whichever aspect instance they belong to.
            this._model.TransformationsList.Add( transformation );
        }

        private void ReadAnnotation( AnnotationData data )
            => this._model.AnnotationsList.Add(
                new IntrospectionAnnotation( this.GetMappedDeclaration( data.TargetDeclaration )! )
                {
                    AnnotationClass = this.GetDeclaration( data.AnnotationClass ),
                    Arguments = data.Arguments,
                    Description = data.Description,
                    LinesOfCodeAvoided = data.LinesOfCodeAvoided
                } );

        private void CompletePredecessor( PendingPredecessor pending, IntrospectionAspectClass aspectClass )
        {
            var data = pending.Data;
            var declaration = pending.Declaration;

            AspectPredecessorKind kind;
            var isInferred = data.Kind == null;

            if ( data.Kind != null )
            {
                kind = data.Kind.Value switch
                {
                    PredecessorKind.Attribute => AspectPredecessorKind.Attribute,
                    PredecessorKind.Multicast => AspectPredecessorKind.Multicast,
                    PredecessorKind.Inherited => AspectPredecessorKind.Inherited,
                    PredecessorKind.Xml => AspectPredecessorKind.Xml,
                    _ => AspectPredecessorKind.ChildAspect
                };
            }
            else if ( declaration == null )
            {
                kind = AspectPredecessorKind.Xml;
            }
            else if ( data.IsForwardReference )
            {
                kind = AspectPredecessorKind.Inherited;
            }
            else if ( pending.AspectInstances.All( i => i.Instance.TargetDeclaration == declaration ) )
            {
                kind = AspectPredecessorKind.Attribute;
            }
            else
            {
                kind = AspectPredecessorKind.Multicast;
            }

            var file = string.IsNullOrEmpty( data.File ) ? null : data.File;

            var predecessor = new IntrospectionPredecessor( kind, isInferred )
            {
                File = file, KeyFile = this.GetKeyFile( file ), Line = data.Line, Column = data.Column
            };

            if ( kind is AspectPredecessorKind.Attribute or AspectPredecessorKind.Multicast or AspectPredecessorKind.Inherited )
            {
                predecessor.Declaration = declaration;
                predecessor.AttributeClass = aspectClass;
            }

            if ( kind == AspectPredecessorKind.ChildAspect && !string.IsNullOrEmpty( data.ProvidedBy ) )
            {
                this._providedPredecessors.Add( (predecessor, data.ProvidedBy!) );
            }

            this._model.PredecessorsList.Add( predecessor );

            foreach ( var pendingInstance in pending.AspectInstances )
            {
                this.AddAspectInstance( pendingInstance.Instance, predecessor );

                // The aspect instances that the aspect provider created, with one predecessor for all of them.
                if ( pendingInstance.ChildOrder.Count > 0 )
                {
                    var provider = new IntrospectionPredecessor( AspectPredecessorKind.ChildAspect, true ) { AspectInstance = pendingInstance.Instance };
                    this._model.PredecessorsList.Add( provider );

                    foreach ( var child in pendingInstance.ChildOrder )
                    {
                        this.AddAspectInstance( child, provider );
                    }
                }
            }
        }

        // The path of a file for a structural key: relative to the project, with forward slashes, when the file is in the project.
        private string? GetKeyFile( string? file )
        {
            if ( file == null || this._projectDirectory == null || !Path.IsPathFullyQualified( file ) )
            {
                return file?.Replace( '\\', '/' );
            }

            var relative = Path.GetRelativePath( this._projectDirectory, file );

            return (relative.StartsWith( "..", StringComparison.Ordinal ) ? file : relative).Replace( '\\', '/' );
        }

        private void AddAspectInstance( IntrospectionAspectInstance instance, IntrospectionPredecessor predecessor )
        {
            instance.PredecessorsList.Add( predecessor );
            predecessor.AspectInstancesList.Add( instance );
            instance.AspectClass.AspectInstancesList.Add( instance );
            this._model.AspectInstancesList.Add( instance );
        }

        private void ResolveProvidedPredecessors()
        {
            if ( this._providedPredecessors.Count == 0 )
            {
                return;
            }

            var instances = this._model.AspectInstancesList.Where( i => !string.IsNullOrEmpty( i.FileId ) )
                .GroupBy( i => i.FileId!, StringComparer.Ordinal )
                .ToDictionary( g => g.Key, g => g.First(), StringComparer.Ordinal );

            foreach ( var (predecessor, providedBy) in this._providedPredecessors )
            {
                predecessor.AspectInstance = instances.GetValueOrDefault( providedBy );
            }
        }

        // Links the accessors and the backing fields to their property or event, by the names that the C# and Visual Basic
        // compilers give them.
        private void LinkMembers()
        {
            foreach ( var declaration in this._model.DeclarationsList )
            {
                var declaringType = declaration.DeclaringType;

                if ( declaringType == null )
                {
                    continue;
                }

                IntrospectionDeclaration? owner = null;

                // An explicit implementation is qualified with its interface, as App#IValue#get_Value: the prefix of the accessor is
                // after the qualifier, which the name of the property or event keeps.
                var qualifierLength = declaration.Name.LastIndexOf( '#' ) + 1;
                var qualifier = declaration.Name.Substring( 0, qualifierLength );
                var name = declaration.Name.Substring( qualifierLength );

                if ( declaration.Kind == IntrospectionDeclarationKind.Method )
                {
                    if ( name.StartsWith( "get_", StringComparison.Ordinal ) || name.StartsWith( "set_", StringComparison.Ordinal ) )
                    {
                        owner = FindProperty( declaringType, qualifier + name.Substring( 4 ), declaration, name[0] == 's' );
                    }
                    else if ( name.StartsWith( "add_", StringComparison.Ordinal ) )
                    {
                        owner = FindMember( declaringType, IntrospectionDeclarationKind.Event, qualifier + name.Substring( 4 ) );
                    }
                    else if ( name.StartsWith( "remove_", StringComparison.Ordinal ) )
                    {
                        owner = FindMember( declaringType, IntrospectionDeclarationKind.Event, qualifier + name.Substring( 7 ) );
                    }
                    else if ( name.StartsWith( "raise_", StringComparison.Ordinal ) )
                    {
                        owner = FindMember( declaringType, IntrospectionDeclarationKind.Event, qualifier + name.Substring( 6 ) );
                    }

                    owner?.AccessorsList.Add( declaration );
                }
                else if ( declaration.Kind == IntrospectionDeclarationKind.Field )
                {
                    if ( name.StartsWith( "<", StringComparison.Ordinal ) && name.EndsWith( ">k__BackingField", StringComparison.Ordinal ) )
                    {
                        // The backing field of a C# automatic property.
                        owner = FindMember( declaringType, IntrospectionDeclarationKind.Property, name.Substring( 1, name.Length - 17 ) );
                    }
                    else if ( name.Length > 1 && name[0] == '_' )
                    {
                        // The backing field of a Visual Basic automatic property, _Name. The file does not say whether the field is
                        // compiler-generated, so a field of this name that the code declares is linked to the property too.
                        owner = FindMember( declaringType, IntrospectionDeclarationKind.Property, name.Substring( 1 ) );

                        if ( owner?.BackingField != null )
                        {
                            owner = null;
                        }
                    }

                    owner?.BackingField = declaration;
                }

                if ( owner != null )
                {
                    declaration.ContainingDeclaration = owner;
                }
            }
        }

        private static IntrospectionDeclaration? FindMember( IntrospectionDeclaration type, IntrospectionDeclarationKind kind, string name )
            => type.MembersList.FirstOrDefault( m => m.Kind == kind && m.Name == name );

        // An indexer is matched by its parameters: the getter has the same, and the setter one more, the value.
        private static IntrospectionDeclaration? FindProperty( IntrospectionDeclaration type, string name, IntrospectionDeclaration accessor, bool isSetter )
        {
            var accessorParameters = GetParameters( accessor.Id );

            return type.MembersList.FirstOrDefault(
                m =>
                {
                    if ( m.Kind != IntrospectionDeclarationKind.Property || m.Name != name )
                    {
                        return false;
                    }

                    var propertyParameters = GetParameters( m.Id );

                    return isSetter
                        ? accessorParameters.Length >= 1 && accessorParameters.Take( accessorParameters.Length - 1 ).SequenceEqual( propertyParameters )
                        : accessorParameters.SequenceEqual( propertyParameters );
                } );

            static string[] GetParameters( string id )
            {
                var open = id.IndexOf( '(', StringComparison.Ordinal );

                return open < 0 ? [] : SplitParameters( id.Substring( open + 1, id.Length - open - 2 ) );
            }

            static string[] SplitParameters( string text )
            {
                var parts = new List<string>();
                var depth = 0;
                var start = 0;

                for ( var i = 0; i < text.Length; i++ )
                {
                    if ( text[i] is '{' or '[' )
                    {
                        depth++;
                    }
                    else if ( text[i] is '}' or ']' )
                    {
                        depth--;
                    }
                    else if ( text[i] == ',' && depth == 0 )
                    {
                        parts.Add( text.Substring( start, i - start ) );
                        start = i + 1;
                    }
                }

                parts.Add( text.Substring( start ) );

                return parts.ToArray();
            }
        }

        private IntrospectionAspectClass GetAspectClass( string reflectionName ) => this.GetAspectClass( this.GetDeclaration( reflectionName ) );

        private IntrospectionAspectClass GetAspectClass( IntrospectionDeclaration declaration )
        {
            if ( !this._aspectClasses.TryGetValue( declaration.QualifiedId, out var aspectClass ) )
            {
                aspectClass = new IntrospectionAspectClass( declaration );
                this._aspectClasses.Add( declaration.QualifiedId, aspectClass );
                this._model.AspectClassesList.Add( aspectClass );
            }

            return aspectClass;
        }

        // The class of an advice: the advice itself when it is a type, or its declaring type.
        private static IntrospectionDeclaration? GetAdviceClass( IntrospectionDeclaration advice )
            => advice.Kind == IntrospectionDeclarationKind.Type ? advice : advice.DeclaringType;

        private IntrospectionDeclaration? GetMappedDeclaration( DeclarationHandle handle )
        {
            if ( handle.IsNil )
            {
                return null;
            }

            var name = this._reader.GetIdentifier( handle );

            // The first source of the chain of mappings. The visited set guards against a cycle, which a malformed file may hold.
            var visited = new HashSet<string>( StringComparer.Ordinal );

            while ( this._mappingSources.TryGetValue( name, out var source ) && visited.Add( name ) )
            {
                name = source;
            }

            return this.GetDeclaration( name );
        }

        private IntrospectionDeclaration? GetDeclaration( DeclarationHandle handle )
            => handle.IsNil ? null : this.GetDeclaration( this._reader.GetIdentifier( handle ) );

        private IntrospectionDeclaration GetDeclaration( string reflectionName )
        {
            if ( this._declarations.TryGetValue( reflectionName, out var declaration ) )
            {
                return declaration;
            }

            var name = V1ReflectionName.Parse( reflectionName );

            var qualifiedId = "[" + name.Assembly + "]" + name.Id;

            if ( this._model.DeclarationsByQualifiedId.TryGetValue( qualifiedId, out declaration ) )
            {
                // Two reflection names of one declaration, such as the name of a type with and without the brackets of its
                // generic parameters, which version 1 does not write.
                this._declarations.Add( reflectionName, declaration );

                return declaration;
            }

            declaration = new IntrospectionDeclaration( name.Assembly, name.Id, name.Kind, name.Name, reflectionName );
            this._declarations.Add( reflectionName, declaration );
            this._model.DeclarationsByQualifiedId.Add( qualifiedId, declaration );
            this._model.DeclarationsList.Add( declaration );

            if ( name.DeclaringTypeName != null )
            {
                var declaringType = this.GetDeclaration( name.DeclaringTypeName );
                declaration.DeclaringType = declaringType;
                declaration.Namespace = declaringType.Namespace;
                declaringType.MembersList.Add( declaration );
            }
            else
            {
                declaration.Namespace = name.Namespace;
            }

            if ( name.ContainingMethodName != null )
            {
                var method = this.GetDeclaration( name.ContainingMethodName );
                declaration.ContainingDeclaration = method;
                declaration.DeclaringType = method.DeclaringType;
                declaration.Namespace = method.Namespace;
            }

            return declaration;
        }

        // The assembly of most target declarations of the aspect instances, or of the declarations when there is none.
        private static string InferAssembly( IntrospectionModel model )
        {
            var candidates = model.AspectInstancesList.Count > 0
                ? model.AspectInstancesList.Select( i => i.TargetDeclaration.Assembly )
                : model.DeclarationsList.Select( d => d.Assembly );

            return candidates.Where( a => a.Length > 0 )
                       .GroupBy( a => a, StringComparer.Ordinal )
                       .OrderByDescending( g => g.Count() )
                       .Select( g => g.Key )
                       .FirstOrDefault()
                   ?? "";
        }

        private sealed class PendingPredecessor( PredecessorData data, IntrospectionDeclaration? declaration )
        {
            public PredecessorData Data { get; } = data;

            public IntrospectionDeclaration? Declaration { get; } = declaration;

            public List<PendingAspectInstance> AspectInstances { get; } = [];
        }

        private sealed class PendingAspectInstance( IntrospectionAspectInstance instance )
        {
            public IntrospectionAspectInstance Instance { get; } = instance;

            // The aspect instances that the aspect provider created, by class of the advice and target declaration.
            public Dictionary<(string Class, string Target), IntrospectionAspectInstance> Children { get; } = new();

            public List<IntrospectionAspectInstance> ChildOrder { get; } = [];
        }
    }
}
