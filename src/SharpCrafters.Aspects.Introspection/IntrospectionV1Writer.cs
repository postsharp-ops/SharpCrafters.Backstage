// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Xml;

namespace SharpCrafters.Aspects.Introspection;

/// <summary>
/// Writes a weaving symbol file of version 1, the <c>.pssym</c> file of PostSharp, forward only: each element is written as
/// soon as it is called, and nothing of the content is kept in memory but the name table and the declarations of the
/// enclosing elements.
/// </summary>
/// <remarks>
/// <para>
/// The sections come in the order of the format: the declaration mappings, then the aspect classes, then the annotations.
/// An aspect class contains predecessors, a predecessor contains aspect instances, and an aspect instance contains
/// transformations. A call that breaks this order throws an <see cref="InvalidOperationException"/>.
/// </para>
/// <para>
/// A declaration is written through a <see cref="DeclarationHandle"/>, which <see cref="GetDeclarationHandle{TKey}"/> returns for
/// a key of the caller and caches, so that its identifier is formatted once. The identifiers are opaque: the caller encodes them.
/// </para>
/// <para>
/// The writer omits an attribute whose value is the one the format implies, and an optional attribute that has no value.
/// </para>
/// </remarks>
public sealed class IntrospectionV1Writer : IDisposable
{
    private readonly XmlWriter _writer;
    private readonly Dictionary<object, DeclarationHandle> _declarations = new();

    // The identifiers of the declarations, by the index of their handle. Index 0 is the nil handle.
    private readonly List<string> _identifiers = [null!];

    // The reference to the name of each declaration (#N), by the index of its handle, once the name is defined. It saves the
    // lookup of the name table and the formatting of the number for every later use of the declaration.
    private readonly List<string?> _references = [null];

    // The references of the name table (#N), by string. Declarations and other strings share one table.
    private readonly Dictionary<string, string> _nameTable = new( StringComparer.Ordinal );

    private Section _section = Section.DeclarationMappings;
    private Level _level = Level.Symbols;

    // The declarations of the enclosing elements, against which the implied values are compared.
    private DeclarationHandle _aspectClass;
    private DeclarationHandle _predecessorDeclaration;
    private DeclarationHandle _aspectInstanceTarget;

    private bool _isDisposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="IntrospectionV1Writer"/> class and writes the start of the root element. When
    /// <paramref name="writer"/> has written nothing yet, the XML declaration is written first. The settings of
    /// <paramref name="writer"/>, such as the indentation and the encoding, are those of the caller.
    /// </summary>
    /// <param name="writer">The <see cref="XmlWriter"/> to write to. It is not disposed with the writer.</param>
    /// <param name="symbols">The attributes of the root element.</param>
    public IntrospectionV1Writer( XmlWriter writer, SymbolsData symbols )
    {
        this._writer = writer ?? throw new ArgumentNullException( nameof(writer) );

        if ( writer.WriteState == WriteState.Start )
        {
            writer.WriteStartDocument();
        }

        writer.WriteStartElement( V1Names.Symbols, V1Names.Namespace );
        this.WriteInt32( V1Names.LinesOfCode, symbols.LinesOfCode );
        this.WriteInt32( V1Names.PostSharpLinesOfCode, symbols.ProductLinesOfCode );
        this.WriteInt32( V1Names.EnhancedTypesCount, symbols.EnhancedTypesCount );
        this.WriteInt32( V1Names.SchemaVersionAttribute, V1Names.SchemaVersion );

        if ( symbols.ProductVersion != null )
        {
            writer.WriteAttributeString( V1Names.PostSharpVersion, symbols.ProductVersion );
        }
    }

    /// <summary>
    /// Gets the handle of a declaration, identified by an object of the caller. The first call for a key formats its identifier
    /// with <paramref name="formatIdentifier"/> and caches the handle; the next calls return the cached handle without formatting.
    /// </summary>
    /// <param name="key">An object of the caller that identifies the declaration, such as an object of a code model, compared with
    /// its <see cref="object.Equals(object)"/>. A <c>null</c> key gives a nil handle.</param>
    /// <param name="formatIdentifier">Formats the identifier of the declaration, as the caller encodes it. Pass a delegate that is
    /// created once, such as a static lambda, so that a call allocates nothing.</param>
    /// <returns>The handle of the declaration. Two keys get different handles even when their identifiers are equal, and these
    /// share a name of the name table. Implied values are omitted when the handles are equal, so by key, not by identifier.</returns>
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

        handle = new DeclarationHandle( this._identifiers.Count );
        this._declarations.Add( key, handle );
        this._identifiers.Add( identifier );
        this._references.Add( null );

        return handle;
    }

    /// <summary>
    /// Writes a declaration mapping. Declaration mappings come before the aspect classes and the annotations.
    /// </summary>
    public void WriteDeclarationMapping( in DeclarationMappingData data )
    {
        this.CheckRequired( data.Source, nameof(DeclarationMappingData.Source) );
        this.CheckRequired( data.Target, nameof(DeclarationMappingData.Target) );
        this.EnterSection( Section.DeclarationMappings, V1Names.Evolution );

        this._writer.WriteStartElement( V1Names.Evolution, V1Names.Namespace );
        this.WriteDeclaration( V1Names.Source, data.Source );
        this.WriteDeclaration( V1Names.Target, data.Target );

        if ( data.Kind != null )
        {
            this._writer.WriteAttributeString( V1Names.Kind, data.Kind.Value.ToString() );
        }

        this._writer.WriteEndElement();
    }

    /// <summary>
    /// Writes the start of an aspect class. Aspect classes come after the declaration mappings and before the annotations.
    /// </summary>
    public void WriteStartAspectClass( in AspectClassData data )
    {
        this.CheckRequired( data.AspectClass, nameof(AspectClassData.AspectClass) );
        this.EnterSection( Section.AspectClasses, V1Names.Class );

        this._writer.WriteStartElement( V1Names.Class, V1Names.Namespace );
        this.WriteDeclaration( V1Names.Class, data.AspectClass );

        if ( data.HasLimitedLicense )
        {
            this._writer.WriteAttributeString( V1Names.LimitedLicense, V1Names.True );
        }

        this._aspectClass = data.AspectClass;
        this._level = Level.AspectClass;
    }

    /// <summary>
    /// Writes the end of the aspect class that <see cref="WriteStartAspectClass"/> started.
    /// </summary>
    public void WriteEndAspectClass()
    {
        this.CheckLevel( Level.AspectClass, "the end of an aspect class" );
        this._writer.WriteEndElement();
        this._aspectClass = default;
        this._level = Level.Symbols;
    }

    /// <summary>
    /// Writes the start of a predecessor, inside an aspect class.
    /// </summary>
    public void WriteStartPredecessor( in PredecessorData data )
    {
        this.CheckLevel( Level.AspectClass, "a predecessor" );
        this.CheckOptional( data.Declaration, nameof(PredecessorData.Declaration) );

        this._writer.WriteStartElement( V1Names.Instance, V1Names.Namespace );
        this.WriteDeclaration( V1Names.Declaration, data.Declaration );

        // The line and the column are a location in the file, so they are written only with it.
        if ( !string.IsNullOrEmpty( data.File ) )
        {
            this._writer.WriteAttributeString( V1Names.File, data.File );

            if ( data.Line != null )
            {
                this.WriteInt32( V1Names.Line, data.Line.Value );

                if ( data.Column != null )
                {
                    this.WriteInt32( V1Names.Column, data.Column.Value );
                }
            }
        }

        if ( data.IsForwardReference )
        {
            this._writer.WriteAttributeString( V1Names.ForwardRef, V1Names.True );
        }

        if ( !string.IsNullOrEmpty( data.Description ) )
        {
            this._writer.WriteAttributeString( V1Names.Description, data.Description );
        }

        if ( !string.IsNullOrEmpty( data.Id ) )
        {
            this._writer.WriteAttributeString( V1Names.Id, data.Id );
        }

        if ( data.Kind != null )
        {
            this._writer.WriteAttributeString( V1Names.Origin, data.Kind.Value.ToString() );
        }

        if ( !string.IsNullOrEmpty( data.ProvidedBy ) )
        {
            this._writer.WriteAttributeString( V1Names.ProvidedBy, data.ProvidedBy );
        }

        this._predecessorDeclaration = data.Declaration;
        this._level = Level.Predecessor;
    }

    /// <summary>
    /// Writes the end of the predecessor that <see cref="WriteStartPredecessor"/> started.
    /// </summary>
    public void WriteEndPredecessor()
    {
        this.CheckLevel( Level.Predecessor, "the end of a predecessor" );
        this._writer.WriteEndElement();
        this._predecessorDeclaration = default;
        this._level = Level.AspectClass;
    }

    /// <summary>
    /// Writes the start of an aspect instance, inside a predecessor. Its target declaration is omitted when it is the
    /// declaration of the predecessor.
    /// </summary>
    public void WriteStartAspectInstance( in AspectInstanceData data )
    {
        this.CheckLevel( Level.Predecessor, "an aspect instance" );
        this.CheckRequired( data.TargetDeclaration, nameof(AspectInstanceData.TargetDeclaration) );

        this._writer.WriteStartElement( V1Names.Target, V1Names.Namespace );

        if ( data.TargetDeclaration != this._predecessorDeclaration )
        {
            this.WriteDeclaration( V1Names.Target, data.TargetDeclaration );
        }

        if ( data.LinesOfCodeAvoided != 0 )
        {
            this.WriteInt32( V1Names.LinesAvoided, data.LinesOfCodeAvoided );
        }

        if ( !string.IsNullOrWhiteSpace( data.Tags ) )
        {
            this._writer.WriteAttributeString( V1Names.Tags, data.Tags );
        }

        if ( !string.IsNullOrEmpty( data.Id ) )
        {
            this._writer.WriteAttributeString( V1Names.Id, data.Id );
        }

        this._aspectInstanceTarget = data.TargetDeclaration;
        this._level = Level.AspectInstance;
    }

    /// <summary>
    /// Writes the end of the aspect instance that <see cref="WriteStartAspectInstance"/> started.
    /// </summary>
    public void WriteEndAspectInstance()
    {
        this.CheckLevel( Level.AspectInstance, "the end of an aspect instance" );
        this._writer.WriteEndElement();
        this._aspectInstanceTarget = default;
        this._level = Level.Predecessor;
    }

    /// <summary>
    /// Writes a transformation, inside an aspect instance. Its target declaration is omitted when it is the target declaration
    /// of the aspect instance, its advice when it is the aspect class, and its order when it is <c>1</c>.
    /// </summary>
    public void WriteTransformation( in TransformationData data )
    {
        this.CheckLevel( Level.AspectInstance, "a transformation" );
        this.CheckOptional( data.TargetDeclaration, nameof(TransformationData.TargetDeclaration) );
        this.CheckOptional( data.AdviceDeclaration, nameof(TransformationData.AdviceDeclaration) );

        this._writer.WriteStartElement( V1Names.JoinPoint, V1Names.Namespace );

        if ( data.TargetDeclaration != this._aspectInstanceTarget )
        {
            this.WriteDeclaration( V1Names.Advised, data.TargetDeclaration );
        }

        if ( data.AdviceDeclaration != this._aspectClass )
        {
            this.WriteDeclaration( V1Names.Advising, data.AdviceDeclaration );
        }

        if ( !string.IsNullOrEmpty( data.Description ) )
        {
            this.WriteName( V1Names.Description, data.Description );
        }

        if ( !string.IsNullOrEmpty( data.Semantic ) )
        {
            this._writer.WriteAttributeString( V1Names.Semantic, data.Semantic );
        }

        if ( data.Order != V1Names.ImpliedOrder )
        {
            this.WriteInt32( V1Names.Ordinal, data.Order );
        }

        if ( data.LinesOfCodeAvoided != null )
        {
            this.WriteInt32( V1Names.LinesAvoided, data.LinesOfCodeAvoided.Value );
        }

        this._writer.WriteEndElement();
    }

    /// <summary>
    /// Writes an annotation. Annotations come after the declaration mappings and the aspect classes.
    /// </summary>
    public void WriteAnnotation( in AnnotationData data )
    {
        this.CheckRequired( data.TargetDeclaration, nameof(AnnotationData.TargetDeclaration) );
        this.CheckOptional( data.AnnotationClass, nameof(AnnotationData.AnnotationClass) );
        this.EnterSection( Section.Annotations, V1Names.Annotation );

        this._writer.WriteStartElement( V1Names.Annotation, V1Names.Namespace );
        this.WriteDeclaration( V1Names.Target, data.TargetDeclaration );
        this.WriteDeclaration( V1Names.Class, data.AnnotationClass );
        this.WriteName( V1Names.Arguments, data.Arguments );
        this.WriteName( V1Names.Description, data.Description );

        if ( data.LinesOfCodeAvoided != 0 )
        {
            this.WriteInt32( V1Names.LinesAvoided, data.LinesOfCodeAvoided );
        }

        this._writer.WriteEndElement();
    }

    /// <summary>
    /// Writes the end of the root element, and of the elements that are still open.
    /// </summary>
    public void Dispose()
    {
        if ( this._isDisposed )
        {
            return;
        }

        this._isDisposed = true;

        // Symbols, and the aspect class, predecessor and aspect instance that are open.
        for ( var level = this._level; level >= Level.Symbols; level-- )
        {
            this._writer.WriteEndElement();
        }
    }

    // A declaration goes through the name table. A nil handle writes nothing.
    private void WriteDeclaration( string attributeName, DeclarationHandle handle )
    {
        if ( handle.IsNil )
        {
            return;
        }

        var reference = this._references[handle.Index];

        if ( reference != null )
        {
            this._writer.WriteAttributeString( attributeName, reference );
        }
        else
        {
            this._references[handle.Index] = this.WriteName( attributeName, this._identifiers[handle.Index] );
        }
    }

    // A string of the name table: #N=<string> at its first use, #N at the next ones. A blank string writes nothing. Returns the
    // reference to the name, #N, or null.
    private string? WriteName( string attributeName, string? value )
    {
        if ( string.IsNullOrWhiteSpace( value ) )
        {
            return null;
        }

        if ( this._nameTable.TryGetValue( value!, out var reference ) )
        {
            this._writer.WriteAttributeString( attributeName, reference );
        }
        else
        {
            reference = V1Names.NameTablePrefix + (this._nameTable.Count + 1).ToString( CultureInfo.InvariantCulture );
            this._nameTable.Add( value!, reference );
            this._writer.WriteAttributeString( attributeName, reference + "=" + value );
        }

        return reference;
    }

    private void WriteInt32( string attributeName, int value ) => this._writer.WriteAttributeString( attributeName, value.ToString( CultureInfo.InvariantCulture ) );

    private void EnterSection( Section section, string elementName )
    {
        this.CheckLevel( Level.Symbols, $"an '{elementName}' element" );

        if ( section < this._section )
        {
            throw new InvalidOperationException( $"An '{elementName}' element cannot be written after the {this._section} section." );
        }

        this._section = section;
    }

    private void CheckLevel( Level expected, string what )
    {
        if ( this._isDisposed )
        {
            throw new ObjectDisposedException( nameof(IntrospectionV1Writer) );
        }

        if ( this._level != expected )
        {
            throw new InvalidOperationException( $"Cannot write {what} inside {Describe( this._level )}; it belongs inside {Describe( expected )}." );
        }

        static string Describe( Level level )
            => level switch
            {
                Level.Symbols => "the root element",
                Level.AspectClass => "an aspect class",
                Level.Predecessor => "a predecessor",
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
        if ( handle.IsNil || handle.Index >= this._identifiers.Count )
        {
            throw new ArgumentException( $"The handle {handle} was not defined by this writer.", parameterName );
        }
    }

    private enum Section
    {
        DeclarationMappings,
        AspectClasses,
        Annotations
    }

    private enum Level
    {
        Symbols,
        AspectClass,
        Predecessor,
        AspectInstance
    }
}
