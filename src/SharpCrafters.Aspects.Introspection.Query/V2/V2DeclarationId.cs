// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using System;

namespace SharpCrafters.Aspects.Introspection.Query.V2;

/// <summary>
/// A declaration identifier of schema version 2: a <c>SerializableDeclarationId</c> of Metalama, optionally qualified with the
/// name of its assembly in brackets, parsed into the parts that the common model needs.
/// </summary>
/// <remarks>
/// <para>The grammar, compared with documentation comment identifiers:</para>
/// <list type="bullet">
/// <item>A declaration of another assembly than the one of the file starts with the name of its assembly in brackets:
/// <c>[Lib]T:Lib.BaseEntity</c>.</item>
/// <item>A parameter is the identifier of its method followed by <c>;Parameter=</c> and its position, and the return parameter by
/// <c>;Return</c>. Other suffixes, such as <c>;TypeParameter=</c>, <c>;File=</c> and <c>;Nullable</c>, are kept in the identifier,
/// and the declaration has the kind of the declaration before the suffix.</item>
/// <item>An assembly is <c>Assembly:</c> followed by its identity. Its identifier in the common model is <c>A:</c> followed by its
/// name, as in version 1.</item>
/// </list>
/// <para>
/// A documentation comment identifier does not distinguish a namespace from a declaring type: <c>T:A.B.C</c> is the type <c>C</c>
/// of the namespace <c>A.B</c> or the type <c>C</c> nested in the type <c>A.B</c>. The parser takes all that precedes the last
/// period as the namespace of a type, so a nested type has no declaring type and its namespace includes the name of the
/// enclosing types. The members of a type are parsed exactly.
/// </para>
/// </remarks>
internal sealed class V2DeclarationId
{
    private const string _assemblyPrefix = "Assembly:";
    private const string _parameterSuffix = "Parameter=";
    private const string _returnSuffix = "Return";

    private V2DeclarationId(
        string assembly,
        string id,
        IntrospectionDeclarationKind kind,
        string name,
        string? namespaceName,
        string? declaringTypeId,
        string? containingDeclarationId )
    {
        this.Assembly = assembly;
        this.Id = id;
        this.Kind = kind;
        this.Name = name;
        this.Namespace = namespaceName;
        this.DeclaringTypeId = declaringTypeId;
        this.ContainingDeclarationId = containingDeclarationId;
    }

    /// <summary>
    /// Gets the name of the assembly that declares the declaration.
    /// </summary>
    public string Assembly { get; }

    /// <summary>
    /// Gets the identifier without the assembly qualifier.
    /// </summary>
    public string Id { get; }

    /// <summary>
    /// Gets the kind of the declaration.
    /// </summary>
    public IntrospectionDeclarationKind Kind { get; }

    /// <summary>
    /// Gets the name of the declaration, without its declaring type, its namespace and its generic arity.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the namespace of the declaration, or <c>null</c>.
    /// </summary>
    public string? Namespace { get; }

    /// <summary>
    /// Gets the qualified identifier of the type that declares a member, or <c>null</c>.
    /// </summary>
    public string? DeclaringTypeId { get; }

    /// <summary>
    /// Gets the qualified identifier of the method or property of a parameter, or <c>null</c>.
    /// </summary>
    public string? ContainingDeclarationId { get; }

    /// <summary>
    /// Parses an identifier of version 2.
    /// </summary>
    /// <param name="identifier">The identifier, as the file gives it.</param>
    /// <param name="defaultAssembly">The assembly of an identifier that has no assembly qualifier: the assembly of the file.</param>
    /// <exception cref="FormatException">The identifier has no known prefix.</exception>
    public static V2DeclarationId Parse( string identifier, string defaultAssembly )
    {
        var assembly = defaultAssembly;
        var rest = identifier;

        if ( rest.StartsWith( '[' ) )
        {
            var end = rest.IndexOf( ']', StringComparison.Ordinal );

            if ( end < 0 )
            {
                throw new FormatException( $"The assembly of '{identifier}' is not closed." );
            }

            assembly = rest.Substring( 1, end - 1 );
            rest = rest.Substring( end + 1 );
        }

        // The qualifier of the identifiers that this one refers to: the same assembly.
        var qualifier = assembly == defaultAssembly ? "" : "[" + assembly + "]";

        if ( rest.StartsWith( _assemblyPrefix, StringComparison.Ordinal ) )
        {
            var identity = rest.Substring( _assemblyPrefix.Length );
            var comma = identity.IndexOf( ',', StringComparison.Ordinal );
            var name = (comma < 0 ? identity : identity.Substring( 0, comma )).Trim();

            return new V2DeclarationId( name, "A:" + name, IntrospectionDeclarationKind.Assembly, name, null, null, null );
        }

        if ( rest.Length < 3 || rest[1] != ':' )
        {
            throw new FormatException( $"'{identifier}' is not a declaration identifier of version 2." );
        }

        // A suffix after the documentation comment identifier.
        var semicolon = rest.IndexOf( ';', StringComparison.Ordinal );

        if ( semicolon >= 0 )
        {
            var baseId = rest.Substring( 0, semicolon );
            var suffix = rest.Substring( semicolon + 1 );
            var parent = ParseDocumentationId( identifier, assembly, qualifier, baseId );

            if ( suffix.StartsWith( _parameterSuffix, StringComparison.Ordinal ) )
            {
                return new V2DeclarationId(
                    assembly,
                    rest,
                    IntrospectionDeclarationKind.Parameter,
                    suffix,
                    parent.Namespace,
                    parent.DeclaringTypeId,
                    qualifier + baseId );
            }

            if ( suffix.StartsWith( _returnSuffix, StringComparison.Ordinal ) )
            {
                return new V2DeclarationId(
                    assembly,
                    rest,
                    IntrospectionDeclarationKind.ReturnParameter,
                    "return",
                    parent.Namespace,
                    parent.DeclaringTypeId,
                    qualifier + baseId );
            }

            return new V2DeclarationId( assembly, rest, parent.Kind, parent.Name, parent.Namespace, parent.DeclaringTypeId, null );
        }

        return ParseDocumentationId( identifier, assembly, qualifier, rest );
    }

    private static V2DeclarationId ParseDocumentationId( string identifier, string assembly, string qualifier, string id )
    {
        var prefix = id[0];
        var body = id.Substring( 2 );

        switch ( prefix )
        {
            case 'T':
                {
                    var lastDot = body.LastIndexOf( '.' );
                    var namespaceName = lastDot < 0 ? null : body.Substring( 0, lastDot );
                    var name = RemoveArity( body.Substring( lastDot + 1 ), "`" );

                    return new V2DeclarationId( assembly, id, IntrospectionDeclarationKind.Type, name, namespaceName, null, null );
                }

            case 'M' or 'P' or 'F' or 'E':
                {
                    // The parameters, which contain periods, follow the name of the member, and the return type that Metalama writes
                    // after ~ follows the parameters or, for a member without parameters, the name.
                    var end = body.IndexOfAny( ['(', '~'] );
                    var head = end < 0 ? body : body.Substring( 0, end );
                    var lastDot = head.LastIndexOf( '.' );

                    if ( lastDot < 0 )
                    {
                        throw new FormatException( $"'{identifier}' has no declaring type." );
                    }

                    var typePath = head.Substring( 0, lastDot );
                    var name = RemoveArity( head.Substring( lastDot + 1 ), "``" );
                    var typeDot = typePath.LastIndexOf( '.' );

                    var kind = prefix switch
                    {
                        'M' when name is "#ctor" or "#cctor" => IntrospectionDeclarationKind.Constructor,
                        'M' => IntrospectionDeclarationKind.Method,
                        'P' => IntrospectionDeclarationKind.Property,
                        'F' => IntrospectionDeclarationKind.Field,
                        _ => IntrospectionDeclarationKind.Event
                    };

                    return new V2DeclarationId(
                        assembly,
                        id,
                        kind,
                        name,
                        typeDot < 0 ? null : typePath.Substring( 0, typeDot ),
                        qualifier + "T:" + typePath,
                        null );
                }

            default:
                throw new FormatException( $"The prefix of '{identifier}' is not known." );
        }
    }

    // The generic arity of a type (`2) or of a method (``2).
    private static string RemoveArity( string name, string marker )
    {
        var index = name.IndexOf( marker, StringComparison.Ordinal );

        return index < 0 ? name : name.Substring( 0, index );
    }
}
