// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using System;
using System.Collections.Generic;
using System.Text.Json;

namespace SharpCrafters.Aspects.Introspection.V2;

/// <summary>
/// Writes a file of schema version 2, the <c>.mlsym</c> file of Metalama, as one JSON document. The writer is forward only: each
/// object is written as soon as it is called, and nothing of the content is kept in memory but the identifiers of the
/// declarations and the identifiers of the aspect instances.
/// </summary>
/// <remarks>
/// <para>
/// The root object contains the aspect classes. An aspect class contains its aspect layers, then its aspect instances. An aspect
/// instance contains its predecessors, then its transformations, then its diagnostics. A call that breaks this order throws an
/// <see cref="InvalidOperationException"/>. Each list is a JSON array, which the writer opens at its first item and closes when
/// the enclosing object ends or when the next list begins.
/// </para>
/// <para>
/// A declaration is written through a <see cref="DeclarationHandle"/>, which <see cref="GetDeclarationHandle{TKey}"/> returns for
/// a key of the caller and caches, so that its identifier is formatted once. The identifier is written as a string at every use:
/// the format has no name table, so that the file can be searched as text. The identifiers are opaque: the caller encodes them.
/// </para>
/// <para>
/// The writer omits a property whose value is the one the format implies (<c>false</c>, <c>0</c>), and an optional property that
/// has no value.
/// </para>
/// </remarks>
public sealed class IntrospectionV2Writer : IDisposable
{
    private static readonly JsonEncodedText[] _predecessorKinds = EncodeNames<V2PredecessorKind>();
    private static readonly JsonEncodedText[] _severities = EncodeNames<V2DiagnosticSeverity>();

    private readonly Utf8JsonWriter _writer;
    private readonly Dictionary<object, DeclarationHandle> _declarations = new();

    // The identifiers of the declarations, by the index of their handle. Index 0 is the nil handle.
    private readonly List<string> _identifiers = [null!];

    // The identifiers of the aspect instances written so far, which must be unique in the file.
    private readonly HashSet<int> _aspectInstanceIds = new();

    // The list that is open at each level, and the last list that was opened at each level, by level.
    private readonly Section[] _openSection = new Section[3];
    private readonly Section[] _lastSection = new Section[3];

    private Level _level = Level.Root;
    private bool _isDisposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="IntrospectionV2Writer"/> class and writes the start of the root object with
    /// its properties. The options of <paramref name="writer"/>, such as the indentation and the encoder, are those of the caller.
    /// </summary>
    /// <param name="writer">The <see cref="Utf8JsonWriter"/> to write to. It must not have written anything yet. It is flushed,
    /// but not disposed, by <see cref="Dispose"/>.</param>
    /// <param name="header">The properties of the root object.</param>
    public IntrospectionV2Writer( Utf8JsonWriter writer, in V2HeaderData header )
    {
        this._writer = writer ?? throw new ArgumentNullException( nameof(writer) );

        if ( string.IsNullOrWhiteSpace( header.Product ) )
        {
            throw new ArgumentException( $"The required property '{nameof(V2HeaderData.Product)}' is not set.", nameof(header) );
        }

        writer.WriteStartObject();
        writer.WriteNumber( V2Names.SchemaVersionProperty, V2Names.SchemaVersion );
        writer.WriteString( V2Names.Product, header.Product );
        this.WriteOptionalString( V2Names.ProductVersion, header.ProductVersion );
        this.WriteOptionalString( V2Names.Assembly, header.Assembly );
        this.WriteOptionalString( V2Names.TargetFramework, header.TargetFramework );
        writer.WriteBoolean( V2Names.CompilationSucceeded, header.CompilationSucceeded );
    }

    /// <summary>
    /// Gets the handle of a declaration, identified by an object of the caller. The first call for a key formats its identifier
    /// with <paramref name="formatIdentifier"/> and caches the handle. The next calls return the cached handle without formatting.
    /// </summary>
    /// <param name="key">An object of the caller that identifies the declaration, such as an object of a code model, compared with
    /// its <see cref="object.Equals(object)"/>. A <c>null</c> key gives a nil handle.</param>
    /// <param name="formatIdentifier">Formats the identifier of the declaration, as the caller encodes it. Pass a delegate that is
    /// created once, such as a static lambda, so that a call allocates nothing.</param>
    /// <returns>The handle of the declaration.</returns>
    public DeclarationHandle GetDeclarationHandle<TKey>( TKey? key, Func<TKey, string> formatIdentifier )
        where TKey : class
    {
        if ( key == null )
        {
            return default;
        }

        if ( this._declarations.TryGetValue( key, out var handle ) )
        {
            return handle;
        }

        if ( formatIdentifier == null )
        {
            throw new ArgumentNullException( nameof(formatIdentifier) );
        }

        var identifier = formatIdentifier( key );

        if ( string.IsNullOrWhiteSpace( identifier ) )
        {
            throw new ArgumentException( "The identifier of a declaration cannot be empty.", nameof(formatIdentifier) );
        }

        handle = new DeclarationHandle( this, this._identifiers.Count );
        this._declarations.Add( key, handle );
        this._identifiers.Add( identifier );

        return handle;
    }

    /// <summary>
    /// Writes the start of an aspect class, inside the root object.
    /// </summary>
    public void WriteStartAspectClass( in V2AspectClassData data )
    {
        this.CheckRequired( data.AspectClass, nameof(V2AspectClassData.AspectClass) );
        this.EnterSection( Level.Root, Section.AspectClasses, "an aspect class" );

        this._writer.WriteStartObject();
        this.WriteDeclaration( V2Names.Id, data.AspectClass );
        this.WriteOptionalString( V2Names.DisplayName, data.DisplayName );
        this.WriteOptionalString( V2Names.Description, data.Description );

        this.EnterLevel( Level.AspectClass );
    }

    /// <summary>
    /// Writes an aspect layer, inside an aspect class and before its aspect instances.
    /// </summary>
    public void WriteAspectLayer( in V2AspectLayerData data )
    {
        this.EnterSection( Level.AspectClass, Section.Layers, "an aspect layer" );

        this._writer.WriteStartObject();
        this.WriteOptionalString( V2Names.Name, data.Name );
        this.WriteOptionalInt32( V2Names.Order, data.Order );
        this._writer.WriteEndObject();
    }

    /// <summary>
    /// Writes the end of the aspect class that <see cref="WriteStartAspectClass"/> started.
    /// </summary>
    public void WriteEndAspectClass() => this.ExitLevel( Level.AspectClass, "the end of an aspect class" );

    /// <summary>
    /// Writes the start of an aspect instance, inside an aspect class and after its aspect layers.
    /// </summary>
    public void WriteStartAspectInstance( in V2AspectInstanceData data )
    {
        this.CheckRequired( data.TargetDeclaration, nameof(V2AspectInstanceData.TargetDeclaration) );

        if ( data.Id <= 0 )
        {
            throw new ArgumentException( $"The identifier of an aspect instance must be positive, but it is {data.Id}.", nameof(data) );
        }

        this.EnterSection( Level.AspectClass, Section.AspectInstances, "an aspect instance" );

        if ( !this._aspectInstanceIds.Add( data.Id ) )
        {
            throw new ArgumentException( $"The identifier {data.Id} is already used by another aspect instance of the file.", nameof(data) );
        }

        this._writer.WriteStartObject();
        this._writer.WriteNumber( V2Names.Id, data.Id );
        this.WriteDeclaration( V2Names.TargetDeclaration, data.TargetDeclaration );
        this.WriteLocation( data.Location );

        if ( data.IsSkipped )
        {
            this._writer.WriteBoolean( V2Names.IsSkipped, true );
        }

        this.EnterLevel( Level.AspectInstance );
    }

    /// <summary>
    /// Writes the end of the aspect instance that <see cref="WriteStartAspectInstance"/> started.
    /// </summary>
    public void WriteEndAspectInstance() => this.ExitLevel( Level.AspectInstance, "the end of an aspect instance" );

    /// <summary>
    /// Writes a predecessor, inside an aspect instance and before its transformations.
    /// </summary>
    public void WritePredecessor( in V2PredecessorData data )
    {
        this.CheckOptional( data.Declaration, nameof(V2PredecessorData.Declaration) );
        this.CheckOptional( data.AttributeClass, nameof(V2PredecessorData.AttributeClass) );
        CheckEnum( data.Kind, nameof(V2PredecessorData.Kind) );

        if ( data.Degree < 0 )
        {
            throw new ArgumentException( $"The degree of a predecessor cannot be negative, but it is {data.Degree}.", nameof(data) );
        }

        if ( data.AspectInstance is <= 0 )
        {
            throw new ArgumentException( $"The identifier of an aspect instance must be positive, but it is {data.AspectInstance}.", nameof(data) );
        }

        this.EnterSection( Level.AspectInstance, Section.Predecessors, "a predecessor" );

        this._writer.WriteStartObject();
        this._writer.WriteString( V2Names.Kind, _predecessorKinds[(int) data.Kind] );
        this.WriteOptionalInt32( V2Names.Degree, data.Degree );
        this.WriteDeclaration( V2Names.Declaration, data.Declaration );
        this.WriteDeclaration( V2Names.AttributeClass, data.AttributeClass );

        if ( data.AspectInstance != null )
        {
            this._writer.WriteNumber( V2Names.AspectInstance, data.AspectInstance.Value );
        }

        this.WriteLocation( data.Location );
        this._writer.WriteEndObject();
    }

    /// <summary>
    /// Writes a transformation, inside an aspect instance, after its predecessors and before its diagnostics.
    /// </summary>
    public void WriteTransformation( in V2TransformationData data )
    {
        if ( string.IsNullOrWhiteSpace( data.Kind ) )
        {
            throw new ArgumentException( $"The required property '{nameof(V2TransformationData.Kind)}' is not set.", nameof(data) );
        }

        this.CheckRequired( data.TargetDeclaration, nameof(V2TransformationData.TargetDeclaration) );
        this.CheckOptional( data.IntroducedDeclaration, nameof(V2TransformationData.IntroducedDeclaration) );
        this.EnterSection( Level.AspectInstance, Section.Transformations, "a transformation" );

        this._writer.WriteStartObject();
        this._writer.WriteString( V2Names.Kind, data.Kind );
        this.WriteOptionalString( V2Names.AdviceKind, data.AdviceKind );
        this.WriteOptionalString( V2Names.Layer, data.Layer );
        this.WriteOptionalInt32( V2Names.Order, data.Order );
        this.WriteDeclaration( V2Names.TargetDeclaration, data.TargetDeclaration );
        this.WriteDeclaration( V2Names.IntroducedDeclaration, data.IntroducedDeclaration );
        this.WriteOptionalString( V2Names.Description, data.Description );
        this._writer.WriteEndObject();
    }

    /// <summary>
    /// Writes a diagnostic, inside an aspect instance and after its transformations.
    /// </summary>
    public void WriteDiagnostic( in V2DiagnosticData data )
    {
        if ( string.IsNullOrWhiteSpace( data.Id ) )
        {
            throw new ArgumentException( $"The required property '{nameof(V2DiagnosticData.Id)}' is not set.", nameof(data) );
        }

        CheckEnum( data.Severity, nameof(V2DiagnosticData.Severity) );
        this.CheckOptional( data.Declaration, nameof(V2DiagnosticData.Declaration) );
        this.EnterSection( Level.AspectInstance, Section.Diagnostics, "a diagnostic" );

        this._writer.WriteStartObject();
        this._writer.WriteString( V2Names.Id, data.Id );
        this._writer.WriteString( V2Names.Severity, _severities[(int) data.Severity] );
        this.WriteOptionalString( V2Names.Message, data.Message );
        this.WriteDeclaration( V2Names.Declaration, data.Declaration );
        this.WriteLocation( data.Location );
        this._writer.WriteEndObject();
    }

    /// <summary>
    /// Writes the end of the objects and arrays that are still open, including the root object, and flushes the
    /// <see cref="Utf8JsonWriter"/>.
    /// </summary>
    public void Dispose()
    {
        if ( this._isDisposed )
        {
            return;
        }

        this._isDisposed = true;

        for ( var level = this._level; level >= Level.Root; level-- )
        {
            this.CloseSection( level );
            this._writer.WriteEndObject();
        }

        this._writer.Flush();
    }

    private void WriteDeclaration( JsonEncodedText propertyName, DeclarationHandle handle )
    {
        if ( !handle.IsNil )
        {
            this._writer.WriteString( propertyName, this._identifiers[handle.Index] );
        }
    }

    private void WriteOptionalString( JsonEncodedText propertyName, string? value )
    {
        if ( !string.IsNullOrEmpty( value ) )
        {
            this._writer.WriteString( propertyName, value );
        }
    }

    private void WriteOptionalInt32( JsonEncodedText propertyName, int value )
    {
        if ( value != 0 )
        {
            this._writer.WriteNumber( propertyName, value );
        }
    }

    // The line and the column are a position in the file, so they are written only with it.
    private void WriteLocation( V2SourceLocation? location )
    {
        if ( location == null )
        {
            return;
        }

        var value = location.Value;

        if ( string.IsNullOrEmpty( value.File ) )
        {
            throw new ArgumentException( $"The required property '{nameof(V2SourceLocation.File)}' of a location is not set." );
        }

        this._writer.WriteStartObject( V2Names.Location );
        this._writer.WriteString( V2Names.File, value.File );

        if ( value.Line != null )
        {
            this._writer.WriteNumber( V2Names.Line, value.Line.Value );

            if ( value.Column != null )
            {
                this._writer.WriteNumber( V2Names.Column, value.Column.Value );
            }
        }

        this._writer.WriteEndObject();
    }

    // Opens the list of an item at the given level, after it closes the list that is open at that level, if it is another one.
    private void EnterSection( Level level, Section section, string what )
    {
        this.CheckLevel( level, what );

        var open = this._openSection[(int) level];

        if ( open == section )
        {
            return;
        }

        if ( section < this._lastSection[(int) level] )
        {
            throw new InvalidOperationException( $"Cannot write {what} after the '{this._lastSection[(int) level]}' list of the same object." );
        }

        this.CloseSection( level );
        this._writer.WriteStartArray( GetPropertyName( section ) );
        this._openSection[(int) level] = section;
        this._lastSection[(int) level] = section;
    }

    private void CloseSection( Level level )
    {
        if ( this._openSection[(int) level] != Section.None )
        {
            this._writer.WriteEndArray();
            this._openSection[(int) level] = Section.None;
        }
    }

    private void EnterLevel( Level level )
    {
        this._level = level;
        this._openSection[(int) level] = Section.None;
        this._lastSection[(int) level] = Section.None;
    }

    private void ExitLevel( Level level, string what )
    {
        this.CheckLevel( level, what );
        this.CloseSection( level );
        this._writer.WriteEndObject();
        this._level = level - 1;
    }

    private void CheckLevel( Level expected, string what )
    {
        if ( this._isDisposed )
        {
            throw new ObjectDisposedException( nameof(IntrospectionV2Writer) );
        }

        if ( this._level != expected )
        {
            throw new InvalidOperationException( $"Cannot write {what} inside {Describe( this._level )}. It belongs inside {Describe( expected )}." );
        }

        static string Describe( Level level )
            => level switch
            {
                Level.Root => "the root object",
                Level.AspectClass => "an aspect class",
                _ => "an aspect instance"
            };
    }

    private void CheckRequired( DeclarationHandle handle, string propertyName )
    {
        if ( handle.IsNil )
        {
            throw new ArgumentException( $"The required property '{propertyName}' is not set.", propertyName );
        }

        this.CheckHandle( handle, propertyName );
    }

    private void CheckOptional( DeclarationHandle handle, string propertyName )
    {
        if ( !handle.IsNil )
        {
            this.CheckHandle( handle, propertyName );
        }
    }

    private void CheckHandle( DeclarationHandle handle, string parameterName )
    {
        if ( !ReferenceEquals( handle.Owner, this ) || handle.Index >= this._identifiers.Count )
        {
            throw new ArgumentException( $"The handle {handle} was not defined by this writer.", parameterName );
        }
    }

    private static void CheckEnum<T>( T value, string propertyName )
        where T : struct, Enum
    {
        if ( !Enum.IsDefined( typeof(T), value ) )
        {
            throw new ArgumentException( $"The value {value} of the property '{propertyName}' is not defined.", propertyName );
        }
    }

    private static JsonEncodedText GetPropertyName( Section section )
        => section switch
        {
            Section.AspectClasses => V2Names.AspectClasses,
            Section.Layers => V2Names.Layers,
            Section.AspectInstances => V2Names.AspectInstances,
            Section.Predecessors => V2Names.Predecessors,
            Section.Transformations => V2Names.Transformations,
            _ => V2Names.Diagnostics
        };

    // The names of the members of an enumeration whose values are 0, 1, 2 and so on, encoded once.
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

    // The lists of the format, in the order in which they come within their object.
    private enum Section
    {
        None,
        AspectClasses,
        Layers,
        AspectInstances,
        Predecessors,
        Transformations,
        Diagnostics
    }

    private enum Level
    {
        Root,
        AspectClass,
        AspectInstance
    }
}
