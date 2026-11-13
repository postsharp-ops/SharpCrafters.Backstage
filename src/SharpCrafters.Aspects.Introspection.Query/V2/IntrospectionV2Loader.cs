// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Aspects.Introspection.Query.V1;
using SharpCrafters.Aspects.Introspection.V2;
using System;
using System.Collections.Generic;
using System.IO;

namespace SharpCrafters.Aspects.Introspection.Query.V2;

/// <summary>
/// Loads a file of schema version 2, the <c>.mlsym</c> file of Metalama, into the common model.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>Each aspect instance of the file becomes an aspect instance of the model, with its transformations and its diagnostics.</item>
/// <item>A predecessor of the file is a child of the aspect instance that it created. The predecessors that are equal, such as the
/// fabric that created several aspect instances, become one predecessor of the model, which lists all these aspect instances.</item>
/// <item>A predecessor of kind <see cref="AspectPredecessorKind.ChildAspect"/> or <see cref="AspectPredecessorKind.RequiredAspect"/>
/// refers to another aspect instance of the file by its identifier in the file.</item>
/// <item>An identifier without an assembly qualifier is a declaration of the assembly of the file. The introduced declarations are
/// declarations of the model, so that the queries on a declaration find them.</item>
/// <item>The location of an aspect instance is the location of its target declaration.</item>
/// <item>A property is linked to its accessors, an event to its accessors, a parameter to its method, and a member to its
/// declaring type, when the file mentions them.</item>
/// </list>
/// </remarks>
public static class IntrospectionV2Loader
{
    /// <summary>
    /// Loads a file from a stream.
    /// </summary>
    /// <param name="stream">The file.</param>
    /// <param name="assembly">The name of the assembly that the file describes, or <c>null</c> to take the one that the file
    /// gives.</param>
    /// <param name="projectDirectory">The directory of the project, or <c>null</c>. The structural key of a predecessor that has a
    /// location holds the path of its file relative to this directory.</param>
    /// <exception cref="FormatException">The file is malformed (<see cref="IntrospectionFormatException"/>), or it holds a
    /// declaration identifier that is not valid.</exception>
    public static IntrospectionModel Load( Stream stream, string? assembly = null, string? projectDirectory = null )
    {
        using var reader = IntrospectionV2Reader.Create( stream );

        return Load( reader, assembly, projectDirectory );
    }

    /// <summary>
    /// Loads a file from a reader positioned at its start.
    /// </summary>
    /// <param name="reader">The reader.</param>
    /// <param name="assembly">The name of the assembly that the file describes, or <c>null</c> to take the one that the file
    /// gives.</param>
    /// <param name="projectDirectory">The directory of the project, or <c>null</c>; see
    /// <see cref="Load(Stream, string?, string?)"/>.</param>
    /// <exception cref="FormatException">The file is malformed (<see cref="IntrospectionFormatException"/>), or it holds a
    /// declaration identifier that is not valid.</exception>
    public static IntrospectionModel Load( IntrospectionV2Reader reader, string? assembly = null, string? projectDirectory = null )
        => new Loader( reader, projectDirectory ).Load( assembly ?? reader.Header.Assembly ?? "" );

    private sealed class Loader
    {
        private readonly IntrospectionV2Reader _reader;
        private readonly string? _projectDirectory;
        private readonly Dictionary<string, IntrospectionDeclaration> _declarations = new( StringComparer.Ordinal );
        private readonly Dictionary<string, IntrospectionAspectClass> _aspectClasses = new( StringComparer.Ordinal );
        private readonly Dictionary<int, IntrospectionAspectInstance> _aspectInstancesById = new();
        private readonly List<(IntrospectionAspectInstance Instance, V2PredecessorData Data)> _pendingPredecessors = [];

        private readonly Dictionary<(AspectPredecessorKind, string?, string?, int?, string?, int?, int?), IntrospectionPredecessor> _predecessors =
            new();

        private IntrospectionModel _model = null!;

        public Loader( IntrospectionV2Reader reader, string? projectDirectory )
        {
            this._reader = reader;
            this._projectDirectory = projectDirectory;
        }

        public IntrospectionModel Load( string assembly )
        {
            this._model = new IntrospectionModel( assembly ) { SchemaVersion = this._reader.SchemaVersion, ProductVersion = this._reader.Header.ProductVersion };

            IntrospectionAspectClass? aspectClass = null;
            IntrospectionAspectInstance? aspectInstance = null;

            while ( this._reader.Read() )
            {
                switch ( this._reader.NodeKind )
                {
                    case IntrospectionV2NodeKind.AspectClass:
                        {
                            var data = this._reader.AspectClass;
                            aspectClass = this.GetAspectClass( this.GetDeclaration( data.AspectClass )! );
                            aspectClass.DisplayName ??= data.DisplayName;
                            aspectClass.Description ??= data.Description;

                            break;
                        }

                    case IntrospectionV2NodeKind.EndAspectClass:
                        aspectClass = null;

                        break;

                    case IntrospectionV2NodeKind.AspectLayer:
                        {
                            var data = this._reader.AspectLayer;
                            aspectClass!.LayersList.Add( new IntrospectionAspectLayer( data.Name, data.Order ) );

                            break;
                        }

                    case IntrospectionV2NodeKind.AspectInstance:
                        aspectInstance = this.ReadAspectInstance( this._reader.AspectInstance, aspectClass! );

                        break;

                    case IntrospectionV2NodeKind.EndAspectInstance:
                        aspectInstance = null;

                        break;

                    case IntrospectionV2NodeKind.Predecessor:
                        // Resolved at the end, because a predecessor can refer to an aspect instance that comes later in the file.
                        this._pendingPredecessors.Add( (aspectInstance!, this._reader.Predecessor) );

                        break;

                    case IntrospectionV2NodeKind.Transformation:
                        this.ReadTransformation( this._reader.Transformation, aspectInstance! );

                        break;

                    case IntrospectionV2NodeKind.Diagnostic:
                        this.ReadDiagnostic( this._reader.Diagnostic, aspectInstance! );

                        break;
                }
            }

            foreach ( var (instance, data) in this._pendingPredecessors )
            {
                this.AddPredecessor( instance, data );
            }

            IntrospectionV1Loader.LinkMembers( this._model );
            StructuralKeys.Assign( this._model );

            return this._model;
        }

        private IntrospectionAspectInstance ReadAspectInstance( V2AspectInstanceData data, IntrospectionAspectClass aspectClass )
        {
            var target = this.GetDeclaration( data.TargetDeclaration )!;

            if ( data.Location is { } location && location.Line != null )
            {
                target.Location ??= new IntrospectionSourceLocation( location.File, location.Line.Value, location.Column ?? 1 );
            }

            var instance = new IntrospectionAspectInstance( aspectClass, target ) { IsSkipped = data.IsSkipped, FileId = data.Id.ToString( System.Globalization.CultureInfo.InvariantCulture ) };

            this._aspectInstancesById[data.Id] = instance;
            aspectClass.AspectInstancesList.Add( instance );
            this._model.AspectInstancesList.Add( instance );

            return instance;
        }

        private void ReadTransformation( V2TransformationData data, IntrospectionAspectInstance instance )
        {
            var transformation = new IntrospectionTransformation( instance, this.GetDeclaration( data.TargetDeclaration )! )
            {
                Kind = data.Kind,
                AdviceKind = data.AdviceKind,
                Layer = data.Layer,
                Order = data.Order,
                IntroducedDeclaration = this.GetDeclaration( data.IntroducedDeclaration ),
                Description = data.Description
            };

            instance.TransformationsList.Add( transformation );
            this._model.TransformationsList.Add( transformation );
        }

        private void ReadDiagnostic( V2DiagnosticData data, IntrospectionAspectInstance instance )
        {
            var diagnostic = new IntrospectionDiagnostic( instance, data.Id, data.Severity.ToString() )
            {
                Message = data.Message,
                Declaration = this.GetDeclaration( data.Declaration ),
                File = data.Location?.File,
                Line = data.Location?.Line,
                Column = data.Location?.Column
            };

            instance.DiagnosticsList.Add( diagnostic );
            this._model.DiagnosticsList.Add( diagnostic );
        }

        private void AddPredecessor( IntrospectionAspectInstance instance, V2PredecessorData data )
        {
            var kind = data.Kind switch
            {
                V2PredecessorKind.Attribute => AspectPredecessorKind.Attribute,
                V2PredecessorKind.ChildAspect => AspectPredecessorKind.ChildAspect,
                V2PredecessorKind.RequiredAspect => AspectPredecessorKind.RequiredAspect,
                V2PredecessorKind.Inherited => AspectPredecessorKind.Inherited,
                V2PredecessorKind.Fabric => AspectPredecessorKind.Fabric,
                _ => AspectPredecessorKind.Interactive
            };

            var declaration = this.GetDeclaration( data.Declaration );
            var attributeClassDeclaration = this.GetDeclaration( data.AttributeClass );
            var location = data.Location;

            var key = (kind, declaration?.QualifiedId, attributeClassDeclaration?.QualifiedId, data.AspectInstance, location?.File, location?.Line,
                       location?.Column);

            if ( !this._predecessors.TryGetValue( key, out var predecessor ) )
            {
                predecessor = new IntrospectionPredecessor( kind, false )
                {
                    Degree = data.Degree,
                    File = location?.File,
                    KeyFile = IntrospectionV1Loader.GetKeyFile( location?.File, this._projectDirectory ),
                    Line = location?.Line,
                    Column = location?.Column
                };

                switch ( kind )
                {
                    case AspectPredecessorKind.Attribute or AspectPredecessorKind.Inherited:
                        predecessor.Declaration = declaration;

                        // The custom attribute of an aspect is usually of the aspect class.
                        predecessor.AttributeClass = attributeClassDeclaration != null ? this.GetAspectClass( attributeClassDeclaration ) : instance.AspectClass;

                        break;

                    case AspectPredecessorKind.ChildAspect or AspectPredecessorKind.RequiredAspect:
                        predecessor.Declaration = declaration;
                        predecessor.AspectInstance = data.AspectInstance is { } id ? this._aspectInstancesById.GetValueOrDefault( id ) : null;

                        break;

                    default:
                        predecessor.Declaration = declaration;

                        break;
                }

                this._predecessors.Add( key, predecessor );
                this._model.PredecessorsList.Add( predecessor );
            }

            predecessor.AspectInstancesList.Add( instance );
            instance.PredecessorsList.Add( predecessor );
        }

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

        private IntrospectionDeclaration? GetDeclaration( DeclarationHandle handle )
            => handle.IsNil ? null : this.GetDeclaration( this._reader.GetIdentifier( handle ) );

        private IntrospectionDeclaration GetDeclaration( string identifier )
        {
            if ( this._declarations.TryGetValue( identifier, out var declaration ) )
            {
                return declaration;
            }

            var id = V2DeclarationId.Parse( identifier, this._model.Assembly );

            var qualifiedId = "[" + id.Assembly + "]" + id.Id;

            if ( this._model.DeclarationsByQualifiedId.TryGetValue( qualifiedId, out declaration ) )
            {
                // The same declaration, with and without the qualifier of the assembly of the file.
                this._declarations.Add( identifier, declaration );

                return declaration;
            }

            declaration = new IntrospectionDeclaration( id.Assembly, id.Id, id.Kind, id.Name, null ) { Namespace = id.Namespace };
            this._declarations.Add( identifier, declaration );
            this._model.DeclarationsByQualifiedId.Add( qualifiedId, declaration );
            this._model.DeclarationsList.Add( declaration );

            if ( id.DeclaringTypeId != null )
            {
                var declaringType = this.GetDeclaration( id.DeclaringTypeId );
                declaration.DeclaringType = declaringType;

                // A parameter is not a member of the type.
                if ( id.ContainingDeclarationId == null )
                {
                    declaringType.MembersList.Add( declaration );
                }
            }

            if ( id.ContainingDeclarationId != null )
            {
                declaration.ContainingDeclaration = this.GetDeclaration( id.ContainingDeclarationId );
            }

            return declaration;
        }
    }
}
