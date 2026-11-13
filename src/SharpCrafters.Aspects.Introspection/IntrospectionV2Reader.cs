// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace SharpCrafters.Aspects.Introspection;

/// <summary>
/// Reads a file of schema version 2, the <c>.mlsym</c> file of Metalama, forward only. Each call to <see cref="Read"/> moves to
/// the next node: the start or the end of an aspect class or of an aspect instance, or an aspect layer, a predecessor, a
/// transformation or a diagnostic. The data of the current node is in the property of its kind.
/// </summary>
/// <remarks>
/// <para>
/// The reader copies the file into one buffer from <see cref="ArrayPool{T}.Shared"/>, and walks it with
/// <see cref="Utf8JsonReader"/>. It builds no object model: the only state is the stack of the enclosing objects and the table
/// of the declaration identifiers.
/// </para>
/// <para>
/// The reader ignores the properties that it does not know, so that a file written by a later version of the product, with the
/// same schema version, remains readable. The properties of an aspect class or of an aspect instance must come before its lists,
/// as the writer writes them; a property that comes after a list is ignored. JSON comments are allowed and ignored.
/// </para>
/// <para>
/// Malformed content throws an <see cref="IntrospectionFormatException"/> that gives the line and the position: invalid JSON, a
/// value of the wrong type, a missing required property, or an unknown predecessor kind or severity.
/// </para>
/// </remarks>
public sealed class IntrospectionV2Reader : IDisposable
{
    private static readonly JsonEncodedText[] _predecessorKinds = EncodeNames<V2PredecessorKind>();
    private static readonly JsonEncodedText[] _severities = EncodeNames<V2DiagnosticSeverity>();

    private readonly Stack<Frame> _frames = new();

    // The handle of each identifier, so that two uses of an identifier return the same handle and the same string instance.
    private readonly Dictionary<string, DeclarationHandle> _handles = new( StringComparer.Ordinal );
    private readonly List<string> _identifiers = [null!];

    private byte[]? _buffer;
    private readonly int _length;

    // The position of the next byte to read, and the state of the Utf8JsonReader at that position.
    private int _offset;
    private JsonReaderState _state;

    private bool _isAtEnd;

    private V2AspectClassData _aspectClass;
    private V2AspectLayerData _aspectLayer;
    private V2AspectInstanceData _aspectInstance;
    private V2PredecessorData _predecessor;
    private V2TransformationData _transformation;
    private V2DiagnosticData _diagnostic;

    private IntrospectionV2Reader( byte[] buffer, int length )
    {
        this._buffer = buffer;
        this._length = length;

        // A UTF-8 byte order mark is allowed before the document.
        if ( length >= 3 && buffer[0] == 0xEF && buffer[1] == 0xBB && buffer[2] == 0xBF )
        {
            this._offset = 3;
        }

        this._state = new JsonReaderState( new JsonReaderOptions { CommentHandling = JsonCommentHandling.Skip } );
    }

    /// <summary>
    /// Creates a reader for a file in a <see cref="Stream"/>, and reads the properties of the root object. The stream is read to
    /// its end, and it is not disposed with the reader.
    /// </summary>
    /// <exception cref="IntrospectionFormatException">The file is not a JSON object, or its root object has no
    /// <c>schemaVersion</c> or <c>product</c>, or its schema version is lower than 2.</exception>
    public static IntrospectionV2Reader Create( Stream stream )
    {
        if ( stream == null )
        {
            throw new ArgumentNullException( nameof(stream) );
        }

        var buffer = ArrayPool<byte>.Shared.Rent( stream.CanSeek ? (int) Math.Max( 16, stream.Length - stream.Position + 1 ) : 64 * 1024 );
        var length = 0;

        try
        {
            while ( true )
            {
                if ( length == buffer.Length )
                {
                    var larger = ArrayPool<byte>.Shared.Rent( buffer.Length * 2 );
                    Buffer.BlockCopy( buffer, 0, larger, 0, length );
                    ArrayPool<byte>.Shared.Return( buffer );
                    buffer = larger;
                }

                var count = stream.Read( buffer, length, buffer.Length - length );

                if ( count == 0 )
                {
                    break;
                }

                length += count;
            }
        }
        catch
        {
            ArrayPool<byte>.Shared.Return( buffer );

            throw;
        }

        var reader = new IntrospectionV2Reader( buffer, length );

        try
        {
            reader.Step( ReadRoot );
        }
        catch
        {
            reader.Dispose();

            throw;
        }

        return reader;
    }

    /// <summary>
    /// Gets the schema version of the file (<c>schemaVersion</c>). It is 2 or more. A version above 2 is read with the rules of
    /// version 2, and the caller decides whether to accept it.
    /// </summary>
    public int SchemaVersion { get; private set; }

    /// <summary>
    /// Gets the properties of the root object.
    /// </summary>
    public V2HeaderData Header { get; private set; }

    /// <summary>
    /// Gets the kind of the current node.
    /// </summary>
    public IntrospectionV2NodeKind NodeKind { get; private set; }

    /// <summary>
    /// Gets the data of the current node when it is <see cref="IntrospectionV2NodeKind.AspectClass"/>.
    /// </summary>
    public V2AspectClassData AspectClass => this.Get( IntrospectionV2NodeKind.AspectClass, this._aspectClass );

    /// <summary>
    /// Gets the data of the current node when it is <see cref="IntrospectionV2NodeKind.AspectLayer"/>.
    /// </summary>
    public V2AspectLayerData AspectLayer => this.Get( IntrospectionV2NodeKind.AspectLayer, this._aspectLayer );

    /// <summary>
    /// Gets the data of the current node when it is <see cref="IntrospectionV2NodeKind.AspectInstance"/>.
    /// </summary>
    public V2AspectInstanceData AspectInstance => this.Get( IntrospectionV2NodeKind.AspectInstance, this._aspectInstance );

    /// <summary>
    /// Gets the data of the current node when it is <see cref="IntrospectionV2NodeKind.Predecessor"/>.
    /// </summary>
    public V2PredecessorData Predecessor => this.Get( IntrospectionV2NodeKind.Predecessor, this._predecessor );

    /// <summary>
    /// Gets the data of the current node when it is <see cref="IntrospectionV2NodeKind.Transformation"/>.
    /// </summary>
    public V2TransformationData Transformation => this.Get( IntrospectionV2NodeKind.Transformation, this._transformation );

    /// <summary>
    /// Gets the data of the current node when it is <see cref="IntrospectionV2NodeKind.Diagnostic"/>.
    /// </summary>
    public V2DiagnosticData Diagnostic => this.Get( IntrospectionV2NodeKind.Diagnostic, this._diagnostic );

    /// <summary>
    /// Gets the identifier of a declaration that this reader returned.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="handle"/> is nil, or another reader or a writer returned it.</exception>
    public string GetIdentifier( DeclarationHandle handle )
    {
        if ( handle.IsNil || !ReferenceEquals( handle.Owner, this ) || handle.Index >= this._identifiers.Count )
        {
            throw new ArgumentException( $"The handle {handle} was not returned by this reader.", nameof(handle) );
        }

        return this._identifiers[handle.Index];
    }

    /// <summary>
    /// Moves to the next node.
    /// </summary>
    /// <returns><c>true</c> when the reader is positioned on a node, <c>false</c> at the end of the file.</returns>
    /// <exception cref="IntrospectionFormatException">The file is malformed.</exception>
    public bool Read()
    {
        if ( this._buffer == null )
        {
            throw new ObjectDisposedException( nameof(IntrospectionV2Reader) );
        }

        if ( this._isAtEnd )
        {
            this.NodeKind = IntrospectionV2NodeKind.None;

            return false;
        }

        this.NodeKind = this.Step( ReadNext );

        if ( this.NodeKind == IntrospectionV2NodeKind.None )
        {
            this._isAtEnd = true;

            return false;
        }

        return true;
    }

    /// <summary>
    /// Returns the buffer of the file to <see cref="ArrayPool{T}.Shared"/>.
    /// </summary>
    public void Dispose()
    {
        if ( this._buffer != null )
        {
            ArrayPool<byte>.Shared.Return( this._buffer );
            this._buffer = null;
        }
    }

    private delegate T Stepper<out T>( IntrospectionV2Reader owner, ref Utf8JsonReader reader );

    // Runs one step on a Utf8JsonReader that resumes at the saved position and state, then saves the new position and state.
    private T Step<T>( Stepper<T> step )
    {
        var reader = new Utf8JsonReader( new ReadOnlySpan<byte>( this._buffer, this._offset, this._length - this._offset ), true, this._state );

        T result;

        try
        {
            result = step( this, ref reader );
        }
        catch ( JsonException e )
        {
            throw CreateException( e );
        }

        this._offset += (int) reader.BytesConsumed;
        this._state = reader.CurrentState;

        return result;
    }

    private static bool ReadRoot( IntrospectionV2Reader owner, ref Utf8JsonReader reader )
    {
        owner.Next( ref reader );

        if ( reader.TokenType != JsonTokenType.StartObject )
        {
            throw owner.CreateException( "The file is not a JSON object.", reader.TokenStartIndex );
        }

        int? schemaVersion = null;
        string? product = null;
        string? productVersion = null;
        string? assembly = null;
        string? targetFramework = null;
        var compilationSucceeded = true;
        var start = reader.TokenStartIndex;

        owner._frames.Push( new Frame( FrameKind.Root ) );

        while ( true )
        {
            owner.Next( ref reader );

            if ( reader.TokenType == JsonTokenType.EndObject )
            {
                // A file without aspect classes.
                owner._frames.Peek().MarkEnded();

                break;
            }

            owner.GetPropertyName( ref reader, out var known );
            owner.Next( ref reader );

            if ( known == V2Property.AspectClasses && reader.TokenType == JsonTokenType.StartArray )
            {
                owner._frames.Push( new Frame( FrameKind.AspectClasses ) );

                break;
            }

            switch ( known )
            {
                case V2Property.SchemaVersion:
                    schemaVersion = owner.GetInt32( ref reader );

                    break;

                case V2Property.Product:
                    product = owner.GetString( ref reader );

                    break;

                case V2Property.ProductVersion:
                    productVersion = owner.GetString( ref reader );

                    break;

                case V2Property.Assembly:
                    assembly = owner.GetString( ref reader );

                    break;

                case V2Property.TargetFramework:
                    targetFramework = owner.GetString( ref reader );

                    break;

                case V2Property.CompilationSucceeded:
                    compilationSucceeded = owner.GetBoolean( ref reader ) ?? true;

                    break;

                default:
                    reader.Skip();

                    break;
            }
        }

        if ( schemaVersion == null )
        {
            throw owner.CreateException( "The root object has no 'schemaVersion' property.", start );
        }

        if ( schemaVersion < V2Names.SchemaVersion )
        {
            throw owner.CreateException( $"The schema version {schemaVersion} is not supported. The version must be {V2Names.SchemaVersion} or higher.", start );
        }

        if ( string.IsNullOrEmpty( product ) )
        {
            throw owner.CreateException( "The root object has no 'product' property.", start );
        }

        owner.SchemaVersion = schemaVersion.Value;

        owner.Header = new V2HeaderData
        {
            Product = product!,
            ProductVersion = productVersion,
            Assembly = assembly,
            TargetFramework = targetFramework,
            CompilationSucceeded = compilationSucceeded
        };

        return true;
    }

    private static IntrospectionV2NodeKind ReadNext( IntrospectionV2Reader owner, ref Utf8JsonReader reader )
    {
        var frames = owner._frames;

        while ( frames.Count > 0 )
        {
            var frame = frames.Peek();

            switch ( frame.Kind )
            {
                case FrameKind.Root:
                case FrameKind.AspectClass:
                case FrameKind.AspectInstance:
                    {
                        if ( !frame.HasEnded )
                        {
                            owner.Next( ref reader );

                            if ( reader.TokenType != JsonTokenType.EndObject )
                            {
                                // A property after the lists, or a list that is not the first one.
                                owner.GetPropertyName( ref reader, out var known );
                                owner.Next( ref reader );
                                owner.TryEnterList( ref reader, frame.Kind, known );

                                continue;
                            }
                        }

                        frames.Pop();

                        switch ( frame.Kind )
                        {
                            case FrameKind.AspectClass:
                                return IntrospectionV2NodeKind.EndAspectClass;

                            case FrameKind.AspectInstance:
                                return IntrospectionV2NodeKind.EndAspectInstance;

                            default:
                                // The end of the root object. The rest of the file can only be white space or comments.
                                if ( reader.Read() )
                                {
                                    throw owner.CreateException( "The file contains content after the root object.", reader.TokenStartIndex );
                                }

                                return IntrospectionV2NodeKind.None;
                        }
                    }

                default:
                    {
                        owner.Next( ref reader );

                        if ( reader.TokenType == JsonTokenType.EndArray )
                        {
                            frames.Pop();

                            continue;
                        }

                        if ( reader.TokenType != JsonTokenType.StartObject )
                        {
                            throw owner.CreateException( $"An item of the '{GetListName( frame.Kind )}' list is not a JSON object.", reader.TokenStartIndex );
                        }

                        switch ( frame.Kind )
                        {
                            case FrameKind.AspectClasses:
                                owner.ReadAspectClass( ref reader );

                                return IntrospectionV2NodeKind.AspectClass;

                            case FrameKind.Layers:
                                owner.ReadAspectLayer( ref reader );

                                return IntrospectionV2NodeKind.AspectLayer;

                            case FrameKind.AspectInstances:
                                owner.ReadAspectInstance( ref reader );

                                return IntrospectionV2NodeKind.AspectInstance;

                            case FrameKind.Predecessors:
                                owner.ReadPredecessor( ref reader );

                                return IntrospectionV2NodeKind.Predecessor;

                            case FrameKind.Transformations:
                                owner.ReadTransformation( ref reader );

                                return IntrospectionV2NodeKind.Transformation;

                            default:
                                owner.ReadDiagnostic( ref reader );

                                return IntrospectionV2NodeKind.Diagnostic;
                        }
                    }
            }
        }

        return IntrospectionV2NodeKind.None;
    }

    // Called on the value of a property of an object that has lists. Enters the list when the property is one of the lists of
    // the object, and skips the value otherwise. Returns whether a list was entered.
    private bool TryEnterList( ref Utf8JsonReader reader, FrameKind objectKind, V2Property property )
    {
        var listKind = (objectKind, property) switch
        {
            (FrameKind.Root, V2Property.AspectClasses) => FrameKind.AspectClasses,
            (FrameKind.AspectClass, V2Property.Layers) => FrameKind.Layers,
            (FrameKind.AspectClass, V2Property.AspectInstances) => FrameKind.AspectInstances,
            (FrameKind.AspectInstance, V2Property.Predecessors) => FrameKind.Predecessors,
            (FrameKind.AspectInstance, V2Property.Transformations) => FrameKind.Transformations,
            (FrameKind.AspectInstance, V2Property.Diagnostics) => FrameKind.Diagnostics,
            _ => FrameKind.Root
        };

        if ( listKind == FrameKind.Root )
        {
            reader.Skip();

            return false;
        }

        if ( reader.TokenType == JsonTokenType.Null )
        {
            return false;
        }

        if ( reader.TokenType != JsonTokenType.StartArray )
        {
            throw this.CreateException( $"The '{GetListName( listKind )}' property is not a JSON array.", reader.TokenStartIndex );
        }

        this._frames.Push( new Frame( listKind ) );

        return true;
    }

    // Reads the properties of an aspect class up to its first list or to its end, and pushes its frame.
    private void ReadAspectClass( ref Utf8JsonReader reader )
    {
        var start = reader.TokenStartIndex;
        var frame = new Frame( FrameKind.AspectClass );
        this._frames.Push( frame );

        DeclarationHandle aspectClass = default;
        string? displayName = null;
        string? description = null;

        var isInList = false;

        while ( !isInList && this.NextProperty( ref reader, frame, out var property ) )
        {
            switch ( property )
            {
                case V2Property.Id:
                    aspectClass = this.GetDeclaration( ref reader );

                    break;

                case V2Property.DisplayName:
                    displayName = this.GetString( ref reader );

                    break;

                case V2Property.Description:
                    description = this.GetString( ref reader );

                    break;

                default:
                    isInList = this.TryEnterList( ref reader, FrameKind.AspectClass, property );

                    break;
            }
        }

        this.CheckRequired( aspectClass, "id", "an aspect class", start );

        this._aspectClass = new V2AspectClassData { AspectClass = aspectClass, DisplayName = displayName, Description = description };
    }

    private void ReadAspectLayer( ref Utf8JsonReader reader )
    {
        string? name = null;
        var order = 0;

        while ( this.NextProperty( ref reader, null, out var property ) )
        {
            switch ( property )
            {
                case V2Property.Name:
                    name = this.GetString( ref reader );

                    break;

                case V2Property.Order:
                    order = this.GetInt32( ref reader ) ?? 0;

                    break;

                default:
                    reader.Skip();

                    break;
            }
        }

        this._aspectLayer = new V2AspectLayerData { Name = name, Order = order };
    }

    // Reads the properties of an aspect instance up to its first list or to its end, and pushes its frame.
    private void ReadAspectInstance( ref Utf8JsonReader reader )
    {
        var start = reader.TokenStartIndex;
        var frame = new Frame( FrameKind.AspectInstance );
        this._frames.Push( frame );

        int? id = null;
        DeclarationHandle targetDeclaration = default;
        V2SourceLocation? location = null;
        var isSkipped = false;

        var isInList = false;

        while ( !isInList && this.NextProperty( ref reader, frame, out var property ) )
        {
            switch ( property )
            {
                case V2Property.Id:
                    id = this.GetInt32( ref reader );

                    break;

                case V2Property.TargetDeclaration:
                    targetDeclaration = this.GetDeclaration( ref reader );

                    break;

                case V2Property.Location:
                    location = this.GetLocation( ref reader );

                    break;

                case V2Property.IsSkipped:
                    isSkipped = this.GetBoolean( ref reader ) ?? false;

                    break;

                default:
                    isInList = this.TryEnterList( ref reader, FrameKind.AspectInstance, property );

                    break;
            }
        }

        if ( id == null )
        {
            throw this.CreateException( "An aspect instance has no 'id' property.", start );
        }

        this.CheckRequired( targetDeclaration, "targetDeclaration", "an aspect instance", start );

        this._aspectInstance = new V2AspectInstanceData
        {
            Id = id.Value, TargetDeclaration = targetDeclaration, Location = location, IsSkipped = isSkipped
        };
    }

    private void ReadPredecessor( ref Utf8JsonReader reader )
    {
        var start = reader.TokenStartIndex;
        V2PredecessorKind? kind = null;
        var degree = 0;
        DeclarationHandle declaration = default;
        DeclarationHandle attributeClass = default;
        int? aspectInstance = null;
        V2SourceLocation? location = null;

        while ( this.NextProperty( ref reader, null, out var property ) )
        {
            switch ( property )
            {
                case V2Property.Kind:
                    kind = this.GetEnum<V2PredecessorKind>( ref reader, _predecessorKinds, "predecessor kind" );

                    break;

                case V2Property.Degree:
                    degree = this.GetInt32( ref reader ) ?? 0;

                    break;

                case V2Property.Declaration:
                    declaration = this.GetDeclaration( ref reader );

                    break;

                case V2Property.AttributeClass:
                    attributeClass = this.GetDeclaration( ref reader );

                    break;

                case V2Property.AspectInstance:
                    aspectInstance = this.GetInt32( ref reader );

                    break;

                case V2Property.Location:
                    location = this.GetLocation( ref reader );

                    break;

                default:
                    reader.Skip();

                    break;
            }
        }

        if ( kind == null )
        {
            throw this.CreateException( "A predecessor has no 'kind' property.", start );
        }

        this._predecessor = new V2PredecessorData
        {
            Kind = kind.Value,
            Degree = degree,
            Declaration = declaration,
            AttributeClass = attributeClass,
            AspectInstance = aspectInstance,
            Location = location
        };
    }

    private void ReadTransformation( ref Utf8JsonReader reader )
    {
        var start = reader.TokenStartIndex;
        string? kind = null;
        string? adviceKind = null;
        string? layer = null;
        var order = 0;
        DeclarationHandle targetDeclaration = default;
        DeclarationHandle introducedDeclaration = default;
        string? description = null;

        while ( this.NextProperty( ref reader, null, out var property ) )
        {
            switch ( property )
            {
                case V2Property.Kind:
                    kind = this.GetString( ref reader );

                    break;

                case V2Property.AdviceKind:
                    adviceKind = this.GetString( ref reader );

                    break;

                case V2Property.Layer:
                    layer = this.GetString( ref reader );

                    break;

                case V2Property.Order:
                    order = this.GetInt32( ref reader ) ?? 0;

                    break;

                case V2Property.TargetDeclaration:
                    targetDeclaration = this.GetDeclaration( ref reader );

                    break;

                case V2Property.IntroducedDeclaration:
                    introducedDeclaration = this.GetDeclaration( ref reader );

                    break;

                case V2Property.Description:
                    description = this.GetString( ref reader );

                    break;

                default:
                    reader.Skip();

                    break;
            }
        }

        if ( string.IsNullOrEmpty( kind ) )
        {
            throw this.CreateException( "A transformation has no 'kind' property.", start );
        }

        this.CheckRequired( targetDeclaration, "targetDeclaration", "a transformation", start );

        this._transformation = new V2TransformationData
        {
            Kind = kind!,
            AdviceKind = adviceKind,
            Layer = layer,
            Order = order,
            TargetDeclaration = targetDeclaration,
            IntroducedDeclaration = introducedDeclaration,
            Description = description
        };
    }

    private void ReadDiagnostic( ref Utf8JsonReader reader )
    {
        var start = reader.TokenStartIndex;
        string? id = null;
        V2DiagnosticSeverity? severity = null;
        string? message = null;
        DeclarationHandle declaration = default;
        V2SourceLocation? location = null;

        while ( this.NextProperty( ref reader, null, out var property ) )
        {
            switch ( property )
            {
                case V2Property.Id:
                    id = this.GetString( ref reader );

                    break;

                case V2Property.Severity:
                    severity = this.GetEnum<V2DiagnosticSeverity>( ref reader, _severities, "severity" );

                    break;

                case V2Property.Message:
                    message = this.GetString( ref reader );

                    break;

                case V2Property.Declaration:
                    declaration = this.GetDeclaration( ref reader );

                    break;

                case V2Property.Location:
                    location = this.GetLocation( ref reader );

                    break;

                default:
                    reader.Skip();

                    break;
            }
        }

        if ( string.IsNullOrEmpty( id ) )
        {
            throw this.CreateException( "A diagnostic has no 'id' property.", start );
        }

        if ( severity == null )
        {
            throw this.CreateException( "A diagnostic has no 'severity' property.", start );
        }

        this._diagnostic = new V2DiagnosticData
        {
            Id = id!, Severity = severity.Value, Message = message, Declaration = declaration, Location = location
        };
    }

    private V2SourceLocation? GetLocation( ref Utf8JsonReader reader )
    {
        if ( reader.TokenType == JsonTokenType.Null )
        {
            return null;
        }

        if ( reader.TokenType != JsonTokenType.StartObject )
        {
            throw this.CreateException( "The 'location' property is not a JSON object.", reader.TokenStartIndex );
        }

        var start = reader.TokenStartIndex;
        string? file = null;
        int? line = null;
        int? column = null;

        while ( this.NextProperty( ref reader, null, out var property ) )
        {
            switch ( property )
            {
                case V2Property.File:
                    file = this.GetString( ref reader );

                    break;

                case V2Property.Line:
                    line = this.GetInt32( ref reader );

                    break;

                case V2Property.Column:
                    column = this.GetInt32( ref reader );

                    break;

                default:
                    reader.Skip();

                    break;
            }
        }

        if ( string.IsNullOrEmpty( file ) )
        {
            throw this.CreateException( "A location has no 'file' property.", start );
        }

        return new V2SourceLocation { File = file!, Line = line, Column = line == null ? null : column };
    }

    // Moves to the value of the next property of the current object. Returns false at the end of the object, and then marks the
    // frame, when there is one, as ended.
    private bool NextProperty( ref Utf8JsonReader reader, Frame? frame, out V2Property property )
    {
        this.Next( ref reader );

        if ( reader.TokenType == JsonTokenType.EndObject )
        {
            frame?.MarkEnded();

            property = V2Property.Unknown;

            return false;
        }

        this.GetPropertyName( ref reader, out property );
        this.Next( ref reader );

        return true;
    }

    private void Next( ref Utf8JsonReader reader )
    {
        if ( !reader.Read() )
        {
            throw this.CreateException( "The file ends before the end of the root object.", reader.BytesConsumed );
        }
    }

    private V2Property GetPropertyName( ref Utf8JsonReader reader, out V2Property property )
    {
        if ( reader.TokenType != JsonTokenType.PropertyName )
        {
            throw this.CreateException( $"A property name was expected, but the token is {reader.TokenType}.", reader.TokenStartIndex );
        }

        property = GetProperty( ref reader );

        return property;
    }

    private string? GetString( ref Utf8JsonReader reader )
    {
        if ( reader.TokenType == JsonTokenType.Null )
        {
            return null;
        }

        if ( reader.TokenType != JsonTokenType.String )
        {
            throw this.CreateException( $"A string was expected, but the token is {reader.TokenType}.", reader.TokenStartIndex );
        }

        return reader.GetString();
    }

    private int? GetInt32( ref Utf8JsonReader reader )
    {
        if ( reader.TokenType == JsonTokenType.Null )
        {
            return null;
        }

        if ( reader.TokenType != JsonTokenType.Number || !reader.TryGetInt32( out var value ) )
        {
            throw this.CreateException( "An integer number was expected.", reader.TokenStartIndex );
        }

        return value;
    }

    private bool? GetBoolean( ref Utf8JsonReader reader )
        => reader.TokenType switch
        {
            JsonTokenType.Null => null,
            JsonTokenType.True => true,
            JsonTokenType.False => false,
            _ => throw this.CreateException( $"A Boolean value was expected, but the token is {reader.TokenType}.", reader.TokenStartIndex )
        };

    private T? GetEnum<T>( ref Utf8JsonReader reader, JsonEncodedText[] names, string what )
        where T : struct, Enum
    {
        if ( reader.TokenType == JsonTokenType.Null )
        {
            return null;
        }

        if ( reader.TokenType == JsonTokenType.String )
        {
            for ( var i = 0; i < names.Length; i++ )
            {
                if ( reader.ValueTextEquals( names[i].EncodedUtf8Bytes ) )
                {
                    return (T) Enum.ToObject( typeof(T), i );
                }
            }
        }

        throw this.CreateException( $"The value is not a known {what}.", reader.TokenStartIndex );
    }

    private DeclarationHandle GetDeclaration( ref Utf8JsonReader reader )
    {
        var identifier = this.GetString( ref reader );

        if ( string.IsNullOrWhiteSpace( identifier ) )
        {
            return default;
        }

        if ( !this._handles.TryGetValue( identifier!, out var handle ) )
        {
            handle = new DeclarationHandle( this, this._identifiers.Count );
            this._handles.Add( identifier!, handle );
            this._identifiers.Add( identifier! );
        }

        return handle;
    }

    private void CheckRequired( DeclarationHandle handle, string propertyName, string what, long start )
    {
        if ( handle.IsNil )
        {
            throw this.CreateException( $"{char.ToUpperInvariant( what[0] )}{what.Substring( 1 )} has no '{propertyName}' property.", start );
        }
    }

    private T Get<T>( IntrospectionV2NodeKind kind, T value )
        => this.NodeKind == kind
            ? value
            : throw new InvalidOperationException( $"The reader is positioned on {this.NodeKind}, not on {kind}." );

    private static V2Property GetProperty( ref Utf8JsonReader reader )
    {
        foreach ( var (name, property) in _properties )
        {
            if ( reader.ValueTextEquals( name.EncodedUtf8Bytes ) )
            {
                return property;
            }
        }

        return V2Property.Unknown;
    }

    // An exception at a position relative to the start of the current step.
    private IntrospectionFormatException CreateException( string message, long relativePosition )
    {
        GetLinePosition( this._buffer!, this._offset + (int) relativePosition, out var line, out var position );

        return new IntrospectionFormatException( message, line, position );
    }

    private static IntrospectionFormatException CreateException( JsonException exception )
    {
        // The JsonReaderState that each step resumes from carries the line and the position, so they are relative to the start
        // of the file. They are zero-based.
        var line = (int) (exception.LineNumber ?? -1) + 1;
        var position = (int) (exception.BytePositionInLine ?? -1) + 1;

        return new IntrospectionFormatException( "The file is not valid JSON.", line, position, exception );
    }

    // The one-based line and position of a byte of the buffer. The position counts bytes, as Utf8JsonReader does.
    private static void GetLinePosition( byte[] buffer, int offset, out int line, out int position )
    {
        line = 1;
        var lineStart = 0;

        for ( var i = 0; i < offset && i < buffer.Length; i++ )
        {
            if ( buffer[i] == (byte) '\n' )
            {
                line++;
                lineStart = i + 1;
            }
        }

        position = offset - lineStart + 1;
    }

    private static string GetListName( FrameKind kind )
        => kind switch
        {
            FrameKind.AspectClasses => "aspectClasses",
            FrameKind.Layers => "layers",
            FrameKind.AspectInstances => "aspectInstances",
            FrameKind.Predecessors => "predecessors",
            FrameKind.Transformations => "transformations",
            _ => "diagnostics"
        };

    private static JsonEncodedText[] EncodeNames<T>()
        where T : struct, Enum
    {
        var values = (T[]) Enum.GetValues( typeof(T) );
        var names = new JsonEncodedText[values.Length];

        foreach ( var value in values )
        {
            names[Convert.ToInt32( value, System.Globalization.CultureInfo.InvariantCulture )] = JsonEncodedText.Encode( value.ToString() );
        }

        return names;
    }

    private static readonly (JsonEncodedText Name, V2Property Property)[] _properties =
    [
        (V2Names.SchemaVersionProperty, V2Property.SchemaVersion),
        (V2Names.Product, V2Property.Product),
        (V2Names.ProductVersion, V2Property.ProductVersion),
        (V2Names.Assembly, V2Property.Assembly),
        (V2Names.TargetFramework, V2Property.TargetFramework),
        (V2Names.CompilationSucceeded, V2Property.CompilationSucceeded),
        (V2Names.AspectClasses, V2Property.AspectClasses),
        (V2Names.Id, V2Property.Id),
        (V2Names.DisplayName, V2Property.DisplayName),
        (V2Names.Description, V2Property.Description),
        (V2Names.Layers, V2Property.Layers),
        (V2Names.AspectInstances, V2Property.AspectInstances),
        (V2Names.Name, V2Property.Name),
        (V2Names.Order, V2Property.Order),
        (V2Names.TargetDeclaration, V2Property.TargetDeclaration),
        (V2Names.Location, V2Property.Location),
        (V2Names.IsSkipped, V2Property.IsSkipped),
        (V2Names.Predecessors, V2Property.Predecessors),
        (V2Names.Transformations, V2Property.Transformations),
        (V2Names.Diagnostics, V2Property.Diagnostics),
        (V2Names.Kind, V2Property.Kind),
        (V2Names.Degree, V2Property.Degree),
        (V2Names.Declaration, V2Property.Declaration),
        (V2Names.AttributeClass, V2Property.AttributeClass),
        (V2Names.AspectInstance, V2Property.AspectInstance),
        (V2Names.AdviceKind, V2Property.AdviceKind),
        (V2Names.Layer, V2Property.Layer),
        (V2Names.IntroducedDeclaration, V2Property.IntroducedDeclaration),
        (V2Names.Severity, V2Property.Severity),
        (V2Names.Message, V2Property.Message),
        (V2Names.File, V2Property.File),
        (V2Names.Line, V2Property.Line),
        (V2Names.Column, V2Property.Column)
    ];

    private enum V2Property
    {
        Unknown,
        SchemaVersion,
        Product,
        ProductVersion,
        Assembly,
        TargetFramework,
        CompilationSucceeded,
        AspectClasses,
        Id,
        DisplayName,
        Description,
        Layers,
        AspectInstances,
        Name,
        Order,
        TargetDeclaration,
        Location,
        IsSkipped,
        Predecessors,
        Transformations,
        Diagnostics,
        Kind,
        Degree,
        Declaration,
        AttributeClass,
        AspectInstance,
        AdviceKind,
        Layer,
        IntroducedDeclaration,
        Severity,
        Message,
        File,
        Line,
        Column
    }

    private enum FrameKind
    {
        // Objects that contain lists.
        Root,
        AspectClass,
        AspectInstance,

        // Lists.
        AspectClasses,
        Layers,
        AspectInstances,
        Predecessors,
        Transformations,
        Diagnostics
    }

    // An enclosing object or list. A class, so that the reading of an object can mark it as ended after it is pushed.
    private sealed class Frame
    {
        public Frame( FrameKind kind )
        {
            this.Kind = kind;
        }

        public FrameKind Kind { get; }

        // For an object: the end of the object was read while its properties were read, before any of its lists.
        public bool HasEnded { get; private set; }

        public void MarkEnded() => this.HasEnded = true;
    }
}
