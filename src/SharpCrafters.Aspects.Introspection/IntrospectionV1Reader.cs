// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Xml;

namespace SharpCrafters.Aspects.Introspection;

/// <summary>
/// Reads a weaving symbol file of version 1, the <c>.pssym</c> file of PostSharp, forward only, as <see cref="XmlReader"/> does:
/// each call to <see cref="Read"/> moves to the next node, and only the current node is exposed.
/// </summary>
/// <remarks>
/// <para>
/// The data structures carry the effective values: a reader returns the value that the format implies for an absent attribute,
/// and decodes the name table, so that the caller sees plain strings. A declaration is returned as a
/// <see cref="DeclarationHandle"/>, whose identifier <see cref="GetIdentifier"/> gives; the same identifier always gives the same
/// handle.
/// </para>
/// <para>
/// The reader ignores the elements and attributes it does not know, so that a newer file of the same version stays readable. A
/// malformed file throws an <see cref="IntrospectionFormatException"/>. Content that is well formed but incomplete, such as a
/// predecessor without an identifier, is returned as it is.
/// </para>
/// </remarks>
public sealed class IntrospectionV1Reader : IDisposable
{
    private readonly XmlReader _reader;
    private readonly bool _ownsReader;

    // The strings of the name table, by number.
    private readonly Dictionary<int, string> _names = new();

    // The handles of the declarations, by identifier, and the identifiers by the index of their handle. Index 0 is the nil handle.
    private readonly Dictionary<string, DeclarationHandle> _handles = new( StringComparer.Ordinal );
    private readonly List<string> _identifiers = [null!];

    // The attributes that the writer puts in the name table, by element. The other attributes of these elements are literal strings,
    // such as Instance/@Description, even when they start with #.
    private static readonly Dictionary<string, string[]> _namedAttributes = new( StringComparer.Ordinal )
    {
        [V1Names.Evolution] = [V1Names.Source, V1Names.Target],
        [V1Names.Class] = [V1Names.Class],
        [V1Names.Instance] = [V1Names.Declaration],
        [V1Names.Target] = [V1Names.Target],
        [V1Names.JoinPoint] = [V1Names.Advised, V1Names.Advising, V1Names.Description],
        [V1Names.Annotation] = [V1Names.Target, V1Names.Class, V1Names.Arguments, V1Names.Description]
    };

    // The elements that are open below the root, with the kind of the node that ends each one.
    private readonly Stack<IntrospectionNodeKind> _openElements = new();

    // The declarations of the enclosing elements, which give the implied values.
    private DeclarationHandle _aspectClass;
    private DeclarationHandle _predecessorDeclaration;
    private DeclarationHandle _aspectInstanceTarget;

    // Set when the XmlReader is already on the node that the next call to Read must consider.
    private bool _isOnNextNode;

    // Set when the current node is the start of an empty element, whose end the next call to Read returns.
    private IntrospectionNodeKind _pendingEnd;

    private bool _isAtEnd;
    private bool _isDocumentEndRead;

    private DeclarationMappingData _declarationMapping;
    private AspectClassData _aspectClassData;
    private PredecessorData _predecessor;
    private AspectInstanceData _aspectInstance;
    private TransformationData _transformation;
    private AnnotationData _annotation;

    private IntrospectionV1Reader( XmlReader reader, bool ownsReader )
    {
        this._reader = reader;
        this._ownsReader = ownsReader;

        try
        {
            this.ReadRoot();
        }
        catch ( XmlException e )
        {
            throw new IntrospectionFormatException( e.Message, e.LineNumber, e.LinePosition, e );
        }
    }

    /// <summary>
    /// Creates a reader of a stream. The <see cref="XmlReader"/> that it creates prohibits DTD processing and resolves no
    /// external resource. The stream is not closed with the reader.
    /// </summary>
    public static IntrospectionV1Reader Create( Stream stream )
    {
        if ( stream == null )
        {
            throw new ArgumentNullException( nameof(stream) );
        }

        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
            IgnoreWhitespace = true,
            CloseInput = false
        };

        return new IntrospectionV1Reader( XmlReader.Create( stream, settings ), true );
    }

    /// <summary>
    /// Creates a reader over an <see cref="XmlReader"/>, whose settings are those of the caller. The <see cref="XmlReader"/> is not
    /// disposed with the reader.
    /// </summary>
    public static IntrospectionV1Reader Create( XmlReader reader )
        => new( reader ?? throw new ArgumentNullException( nameof(reader) ), false );

    /// <summary>
    /// Gets the schema version of the file (<c>SchemaVersion</c>). A file that does not give it is of version <c>1</c>.
    /// </summary>
    public int SchemaVersion { get; private set; }

    /// <summary>
    /// Gets the attributes of the root element, which the reader reads when it is created.
    /// </summary>
    public SymbolsData Symbols { get; private set; }

    /// <summary>
    /// Gets the kind of the current node.
    /// </summary>
    public IntrospectionNodeKind NodeKind { get; private set; }

    /// <summary>
    /// Gets the current declaration mapping, when <see cref="NodeKind"/> is <see cref="IntrospectionNodeKind.DeclarationMapping"/>.
    /// </summary>
    public DeclarationMappingData DeclarationMapping => this.Get( IntrospectionNodeKind.DeclarationMapping, this._declarationMapping );

    /// <summary>
    /// Gets the current aspect class, when <see cref="NodeKind"/> is <see cref="IntrospectionNodeKind.AspectClass"/>.
    /// </summary>
    public AspectClassData AspectClass => this.Get( IntrospectionNodeKind.AspectClass, this._aspectClassData );

    /// <summary>
    /// Gets the current predecessor, when <see cref="NodeKind"/> is <see cref="IntrospectionNodeKind.Predecessor"/>.
    /// </summary>
    public PredecessorData Predecessor => this.Get( IntrospectionNodeKind.Predecessor, this._predecessor );

    /// <summary>
    /// Gets the current aspect instance, when <see cref="NodeKind"/> is <see cref="IntrospectionNodeKind.AspectInstance"/>.
    /// </summary>
    public AspectInstanceData AspectInstance => this.Get( IntrospectionNodeKind.AspectInstance, this._aspectInstance );

    /// <summary>
    /// Gets the current transformation, when <see cref="NodeKind"/> is <see cref="IntrospectionNodeKind.Transformation"/>.
    /// </summary>
    public TransformationData Transformation => this.Get( IntrospectionNodeKind.Transformation, this._transformation );

    /// <summary>
    /// Gets the current annotation, when <see cref="NodeKind"/> is <see cref="IntrospectionNodeKind.Annotation"/>.
    /// </summary>
    public AnnotationData Annotation => this.Get( IntrospectionNodeKind.Annotation, this._annotation );

    /// <summary>
    /// Gets the identifier of a declaration that this reader returned.
    /// </summary>
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
    /// <returns><c>false</c> at the end of the file.</returns>
    public bool Read()
    {
        try
        {
            return this.ReadCore();
        }
        catch ( XmlException e )
        {
            throw new IntrospectionFormatException( e.Message, e.LineNumber, e.LinePosition, e );
        }
    }

    /// <summary>
    /// Disposes the <see cref="XmlReader"/> when the reader created it.
    /// </summary>
    public void Dispose()
    {
        if ( this._ownsReader )
        {
            this._reader.Dispose();
        }
    }

    private void ReadRoot()
    {
        if ( this._reader.MoveToContent() != XmlNodeType.Element || this._reader.LocalName != V1Names.Symbols
                                                                  || this._reader.NamespaceURI != V1Names.Namespace )
        {
            throw this.CreateException( $"The root element is not '{V1Names.Symbols}' of the namespace '{V1Names.Namespace}'." );
        }

        this.SchemaVersion = this.GetInt32( V1Names.SchemaVersionAttribute ) ?? V1Names.SchemaVersion;

        this.Symbols = new SymbolsData
        {
            ProductVersion = this._reader.GetAttribute( V1Names.PostSharpVersion ),
            LinesOfCode = this.GetInt32( V1Names.LinesOfCode ) ?? 0,
            ProductLinesOfCode = this.GetInt32( V1Names.PostSharpLinesOfCode ) ?? 0,
            EnhancedTypesCount = this.GetInt32( V1Names.EnhancedTypesCount ) ?? 0
        };

        this._isAtEnd = this._reader.IsEmptyElement;
    }

    // After the root element, the rest of a document that the reader created is read, so that a second root element or trailing
    // garbage is an error rather than ignored. An XmlReader of the caller is left at the end of the root element.
    private void ReadDocumentEnd()
    {
        if ( this._ownsReader )
        {
            while ( this._reader.Read() )
            {
                if ( this._reader.NodeType == XmlNodeType.Element )
                {
                    throw this.CreateException( $"The document has content after the '{V1Names.Symbols}' element." );
                }
            }
        }
    }

    private bool ReadCore()
    {
        if ( this._pendingEnd != IntrospectionNodeKind.None )
        {
            this.NodeKind = this._pendingEnd;
            this._pendingEnd = IntrospectionNodeKind.None;
            this.OnEnd( this.NodeKind );

            return true;
        }

        while ( !this._isAtEnd )
        {
            if ( this._isOnNextNode )
            {
                this._isOnNextNode = false;
            }
            else if ( !this._reader.Read() )
            {
                break;
            }

            switch ( this._reader.NodeType )
            {
                case XmlNodeType.Element:
                    if ( this.ReadElement() )
                    {
                        return true;
                    }

                    break;

                case XmlNodeType.EndElement:
                    if ( this._openElements.Count == 0 )
                    {
                        // The end of the root element.
                        this._isAtEnd = true;

                        break;
                    }

                    this.NodeKind = this._openElements.Pop();
                    this.OnEnd( this.NodeKind );

                    return true;
            }
        }

        if ( !this._isDocumentEndRead )
        {
            this._isDocumentEndRead = true;
            this.ReadDocumentEnd();
        }

        this._isAtEnd = true;
        this.NodeKind = IntrospectionNodeKind.None;

        return false;
    }

    // Reads the element on which the XmlReader is positioned. Returns false when it is skipped.
    private bool ReadElement()
    {
        var parent = this._openElements.Count == 0 ? IntrospectionNodeKind.None : this._openElements.Peek();
        var isKnown = this._reader.NamespaceURI == V1Names.Namespace;

        // The definitions of the name table are registered before the properties are read, so that the order of the attributes does
        // not matter: Target="#1=M:A" Source="#1" is valid.
        this.RegisterNames( isKnown );

        switch ( isKnown ? this._reader.LocalName : null )
        {
            case V1Names.Evolution when parent == IntrospectionNodeKind.None:
                this._declarationMapping = new DeclarationMappingData
                {
                    Source = this.GetRequiredDeclaration( V1Names.Source ),
                    Target = this.GetRequiredDeclaration( V1Names.Target ),
                    Kind = this.GetEnum<DeclarationMappingKind>( V1Names.Kind )
                };

                return this.ReturnLeaf( IntrospectionNodeKind.DeclarationMapping );

            case V1Names.Class when parent == IntrospectionNodeKind.None:
                this._aspectClassData = new AspectClassData
                {
                    AspectClass = this.GetRequiredDeclaration( V1Names.Class ), HasLimitedLicense = this.GetBoolean( V1Names.LimitedLicense )
                };

                this._aspectClass = this._aspectClassData.AspectClass;

                return this.ReturnContainer( IntrospectionNodeKind.AspectClass, IntrospectionNodeKind.EndAspectClass );

            case V1Names.Instance when parent == IntrospectionNodeKind.EndAspectClass:
                this._predecessor = new PredecessorData
                {
                    Declaration = this.GetDeclaration( V1Names.Declaration ),
                    File = this._reader.GetAttribute( V1Names.File ),
                    Line = this.GetInt32( V1Names.Line ),
                    Column = this.GetInt32( V1Names.Column ),
                    IsForwardReference = this.GetBoolean( V1Names.ForwardRef ),
                    Description = this._reader.GetAttribute( V1Names.Description ),
                    Id = this._reader.GetAttribute( V1Names.Id ),
                    Kind = this.GetEnum<PredecessorKind>( V1Names.Origin ),
                    ProvidedBy = this._reader.GetAttribute( V1Names.ProvidedBy )
                };

                this._predecessorDeclaration = this._predecessor.Declaration;

                return this.ReturnContainer( IntrospectionNodeKind.Predecessor, IntrospectionNodeKind.EndPredecessor );

            case V1Names.Target when parent == IntrospectionNodeKind.EndPredecessor:
                var target = this.GetDeclaration( V1Names.Target );

                if ( target.IsNil )
                {
                    target = this._predecessorDeclaration.IsNil
                        ? throw this.CreateException( $"The '{V1Names.Target}' element has no '{V1Names.Target}' attribute, and its predecessor has no declaration." )
                        : this._predecessorDeclaration;
                }

                this._aspectInstance = new AspectInstanceData
                {
                    TargetDeclaration = target,
                    LinesOfCodeAvoided = this.GetInt32( V1Names.LinesAvoided ) ?? 0,
                    Tags = this._reader.GetAttribute( V1Names.Tags ),
                    Id = this._reader.GetAttribute( V1Names.Id )
                };

                this._aspectInstanceTarget = target;

                return this.ReturnContainer( IntrospectionNodeKind.AspectInstance, IntrospectionNodeKind.EndAspectInstance );

            case V1Names.JoinPoint when parent == IntrospectionNodeKind.EndAspectInstance:
                var advised = this.GetDeclaration( V1Names.Advised );
                var advising = this.GetDeclaration( V1Names.Advising );

                this._transformation = new TransformationData
                {
                    TargetDeclaration = advised.IsNil ? this._aspectInstanceTarget : advised,
                    AdviceDeclaration = advising.IsNil ? this._aspectClass : advising,
                    Description = this.GetName( V1Names.Description ),
                    Semantic = this._reader.GetAttribute( V1Names.Semantic ),
                    Order = this.GetInt32( V1Names.Ordinal ) ?? V1Names.ImpliedOrder,
                    LinesOfCodeAvoided = this.GetInt32( V1Names.LinesAvoided )
                };

                return this.ReturnLeaf( IntrospectionNodeKind.Transformation );

            case V1Names.Annotation when parent == IntrospectionNodeKind.None:
                this._annotation = new AnnotationData
                {
                    TargetDeclaration = this.GetRequiredDeclaration( V1Names.Target ),
                    AnnotationClass = this.GetDeclaration( V1Names.Class ),
                    Arguments = this.GetName( V1Names.Arguments ),
                    Description = this.GetName( V1Names.Description ),
                    LinesOfCodeAvoided = this.GetInt32( V1Names.LinesAvoided ) ?? 0
                };

                return this.ReturnLeaf( IntrospectionNodeKind.Annotation );

            default:
                // An element that the reader does not know, or that is not where the format puts it: skipped with its content.
                this.SkipElement();

                return false;
        }
    }

    // An element without children of its own: its content, if any, is skipped.
    private bool ReturnLeaf( IntrospectionNodeKind kind )
    {
        this.SkipElement();
        this.NodeKind = kind;

        return true;
    }

    private bool ReturnContainer( IntrospectionNodeKind kind, IntrospectionNodeKind endKind )
    {
        if ( this._reader.IsEmptyElement )
        {
            this._pendingEnd = endKind;
        }
        else
        {
            this._openElements.Push( endKind );
        }

        this.NodeKind = kind;

        return true;
    }

    // Skips an element and its content, whose definitions of the name table are registered: a later element can refer to a name that
    // an element the reader does not know defines.
    private void SkipElement()
    {
        if ( this._reader.IsEmptyElement )
        {
            return;
        }

        var depth = this._reader.Depth;

        while ( this._reader.Read() && !(this._reader.NodeType == XmlNodeType.EndElement && this._reader.Depth == depth) )
        {
            if ( this._reader.NodeType == XmlNodeType.Element )
            {
                this.RegisterNames( false );
            }
        }

        // Leaves the XmlReader on the node after the end of the element, as XmlReader.Skip does.
        this._reader.Read();
        this._isOnNextNode = true;
    }

    // Registers the definitions of the name table of the element on which the XmlReader is positioned: those of the attributes that
    // the writer puts in the name table for a known element, and every value of the form #N=<string> for an unknown element, whose
    // attributes the reader does not know.
    private void RegisterNames( bool isKnown )
    {
        if ( isKnown && _namedAttributes.TryGetValue( this._reader.LocalName, out var names ) )
        {
            foreach ( var name in names )
            {
                this.RegisterName( this._reader.GetAttribute( name ) );
            }

            return;
        }

        if ( !this._reader.MoveToFirstAttribute() )
        {
            return;
        }

        do
        {
            this.RegisterName( this._reader.Value );
        }
        while ( this._reader.MoveToNextAttribute() );

        this._reader.MoveToElement();
    }

    private void RegisterName( string? value )
    {
        if ( value == null || value.Length == 0 || value[0] != V1Names.NameTablePrefix )
        {
            return;
        }

        var separator = value.IndexOf( "=", StringComparison.Ordinal );

        if ( separator > 1 && int.TryParse( value.Substring( 1, separator - 1 ), NumberStyles.None, CultureInfo.InvariantCulture, out var number ) )
        {
            this._names[number] = value.Substring( separator + 1 );
        }
    }

    private void OnEnd( IntrospectionNodeKind kind )
    {
        switch ( kind )
        {
            case IntrospectionNodeKind.EndAspectClass:
                this._aspectClass = default;

                break;

            case IntrospectionNodeKind.EndPredecessor:
                this._predecessorDeclaration = default;

                break;

            case IntrospectionNodeKind.EndAspectInstance:
                this._aspectInstanceTarget = default;

                break;
        }
    }

    private T Get<T>( IntrospectionNodeKind kind, T value )
        => this.NodeKind == kind
            ? value
            : throw new InvalidOperationException( $"The reader is positioned on a node of kind {this.NodeKind}, not {kind}." );

    private DeclarationHandle GetRequiredDeclaration( string attributeName )
    {
        var handle = this.GetDeclaration( attributeName );

        return handle.IsNil
            ? throw this.CreateException( $"The '{this._reader.LocalName}' element has no '{attributeName}' attribute." )
            : handle;
    }

    private DeclarationHandle GetDeclaration( string attributeName )
    {
        var identifier = this.GetName( attributeName );

        // A blank identifier stands for no declaration, as the writer rejects it.
        if ( identifier == null || string.IsNullOrWhiteSpace( identifier ) )
        {
            return default;
        }

        if ( !this._handles.TryGetValue( identifier, out var handle ) )
        {
            handle = new DeclarationHandle( this, this._identifiers.Count );
            this._identifiers.Add( identifier );
            this._handles.Add( identifier, handle );
        }

        return handle;
    }

    // Decodes a value of the name table: #N=<string> defines the name N, and #N refers to it. Any other value is a plain string.
    private string? GetName( string attributeName )
    {
        var value = this._reader.GetAttribute( attributeName );

        if ( value == null || value.Length == 0 || value[0] != V1Names.NameTablePrefix )
        {
            return value;
        }

        var separator = value.IndexOf( "=", StringComparison.Ordinal );
        var numberText = separator < 0 ? value.Substring( 1 ) : value.Substring( 1, separator - 1 );

        if ( !int.TryParse( numberText, NumberStyles.None, CultureInfo.InvariantCulture, out var number ) )
        {
            throw this.CreateException( $"The value '{value}' of the '{attributeName}' attribute is not a valid reference to the name table." );
        }

        if ( separator >= 0 )
        {
            var name = value.Substring( separator + 1 );
            this._names[number] = name;

            return name;
        }

        return this._names.TryGetValue( number, out var defined )
            ? defined
            : throw this.CreateException( $"The '{attributeName}' attribute refers to the name #{number}, which is not defined before." );
    }

    private int? GetInt32( string attributeName )
    {
        var value = this._reader.GetAttribute( attributeName );

        if ( value == null )
        {
            return null;
        }

        return int.TryParse( value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var result )
            ? result
            : throw this.CreateException( $"The value '{value}' of the '{attributeName}' attribute is not a valid integer." );
    }

    private bool GetBoolean( string attributeName )
    {
        var value = this._reader.GetAttribute( attributeName );

        if ( value == null )
        {
            return false;
        }

        return bool.TryParse( value, out var result )
            ? result
            : throw this.CreateException( $"The value '{value}' of the '{attributeName}' attribute is not a valid Boolean." );
    }

    // An enumerated value that the reader does not know is ignored, as an unknown attribute is. Enum.TryParse accepts any number, so a
    // number that is not a member of the enumeration is ignored too.
    private T? GetEnum<T>( string attributeName )
        where T : struct, Enum
        => Enum.TryParse<T>( this._reader.GetAttribute( attributeName ), false, out var result ) && Enum.IsDefined( typeof(T), result ) ? result : null;

    private IntrospectionFormatException CreateException( string message )
        => this._reader is IXmlLineInfo lineInfo
            ? new IntrospectionFormatException( message, lineInfo.LineNumber, lineInfo.LinePosition )
            : new IntrospectionFormatException( message, 0, 0 );
}
