// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace SharpCrafters.Aspects.Introspection.Query.V1;

/// <summary>
/// A declaration identifier of version 1, the reflection name that <c>WriteWeavingSymbolsTask</c> of PostSharp writes
/// (<c>ReflectionNameOptions.SymFile | CurlGenericBrackets | XmlDocPrefix</c>), parsed and converted to a documentation
/// comment identifier (Documentation/future/Introspection-Cli.md of PostSharp, section 7.5).
/// </summary>
/// <remarks>
/// <para>The grammar of version 1, compared with documentation comment identifiers:</para>
/// <list type="bullet">
/// <item>The declaring assembly comes first, in brackets: <c>M:[App]App.Repository::Get(System.Int32)</c>. An assembly is
/// <c>A:App</c>.</item>
/// <item>A nested type is separated by <c>+</c>, a member by <c>::</c>.</item>
/// <item>The generic parameters of a type or a method are written by name, in braces: <c>T:[App]App.Box{T}</c>,
/// <c>M:[App]App.C::M{T,U}(T, U)</c>. Documentation comment identifiers give a type its arity (<c>Box`1</c>) and a method
/// its arity (<c>M``2</c>), and refer to a generic parameter by its position (<c>`0</c>, <c>``0</c>).</item>
/// <item>The parameters are separated by a comma and a space. A parameter type of another assembly may be qualified with
/// it (<c>[System.Runtime]System.Decimal</c>). A by-reference type ends with <c>&amp;</c> (<c>@</c> in a documentation
/// comment identifier), and an array of several dimensions is <c>[,]</c> (<c>[0:,0:]</c>).</item>
/// <item>A property without parameters has empty parentheses: <c>P:[App]App.C::Name()</c>.</item>
/// <item>The names of methods and properties are already written as in documentation comment identifiers: <c>#ctor</c>, and
/// <c>App#IService#Run</c> for an explicit implementation. The <c>&lt;</c> and <c>&gt;</c> of their names are braces, as
/// in the property <c>{Name}k__BackingField</c> that PostSharp introduces; those of fields are not.</item>
/// <item>A parameter is the method followed by <c>@</c> and the name of the parameter, or <c>@return</c>:
/// <c>R:[App]App.C::M(System.String)@value</c>. A file of version 1 gives the name and not the position of the parameter,
/// so the identifier is <c>M:App.C.M(System.String);Parameter=value</c>, with the name where
/// <c>SerializableDeclarationId</c> has the position, and <c>;Return</c> for the return parameter.</item>
/// <item>A conversion operator is followed by <c>~</c> and its return type, as in a documentation comment identifier:
/// <c>M:[App]App.C::op_Implicit([App]App.C)~System.Int32</c>.</item>
/// </list>
/// </remarks>
internal sealed partial class V1ReflectionName
{
    // An assembly that qualifies a type in the parameters: brackets around a name, which may start with a digit or any letter,
    // unlike the brackets of an array, which hold commas or nothing.
    [GeneratedRegex( @"\[[^\[\],\s]+\]", RegexOptions.CultureInvariant )]
    private static partial Regex AssemblyQualifierRegex();

    // A name in a parameter type: a namespace, a type, or a generic parameter, with the characters of a C# identifier.
    [GeneratedRegex( @"[\p{L}\p{Nl}_][\p{L}\p{Nl}\p{Mn}\p{Mc}\p{Nd}\p{Pc}\p{Cf}.`]*", RegexOptions.CultureInvariant )]
    private static partial Regex NameRegex();

    // The ranks of an array of several dimensions.
    [GeneratedRegex( @"\[,+\]", RegexOptions.CultureInvariant )]
    private static partial Regex ArrayRankRegex();

    private V1ReflectionName(
        string assembly,
        string id,
        IntrospectionDeclarationKind kind,
        string name,
        string? namespaceName,
        string? declaringTypeName,
        string? containingMethodName )
    {
        this.Assembly = assembly;
        this.Id = id;
        this.Kind = kind;
        this.Name = name;
        this.Namespace = namespaceName;
        this.DeclaringTypeName = declaringTypeName;
        this.ContainingMethodName = containingMethodName;
    }

    /// <summary>
    /// Gets the declaring assembly, or an empty string when the name gives none.
    /// </summary>
    public string Assembly { get; }

    /// <summary>
    /// Gets the documentation comment identifier.
    /// </summary>
    public string Id { get; }

    public IntrospectionDeclarationKind Kind { get; }

    public string Name { get; }

    public string? Namespace { get; }

    /// <summary>
    /// Gets the reflection name of the declaring type of a member or of a nested type, or <c>null</c>.
    /// </summary>
    public string? DeclaringTypeName { get; }

    /// <summary>
    /// Gets the reflection name of the method of a parameter, or <c>null</c>.
    /// </summary>
    public string? ContainingMethodName { get; }

    /// <summary>
    /// Parses a reflection name of version 1.
    /// </summary>
    /// <exception cref="FormatException">The name is not a reflection name of version 1.</exception>
    public static V1ReflectionName Parse( string reflectionName )
    {
        if ( reflectionName.Length < 3 || reflectionName[1] != ':' )
        {
            throw new FormatException( $"'{reflectionName}' is not a declaration identifier of version 1." );
        }

        var prefix = reflectionName[0];
        var rest = reflectionName.Substring( 2 );

        if ( prefix == 'A' )
        {
            return new V1ReflectionName( rest, "A:" + rest, IntrospectionDeclarationKind.Assembly, rest, null, null, null );
        }

        var assembly = "";

        if ( rest.StartsWith( "[", StringComparison.Ordinal ) )
        {
            var end = rest.IndexOf( ']', StringComparison.Ordinal );

            if ( end < 0 )
            {
                throw new FormatException( $"The assembly of '{reflectionName}' is not closed." );
            }

            assembly = rest.Substring( 1, end - 1 );
            rest = rest.Substring( end + 1 );
        }

        switch ( prefix )
        {
            case 'T':
                return ParseType( reflectionName, assembly, rest );

            case 'M' or 'P' or 'F' or 'E':
                return ParseMember( reflectionName, prefix, assembly, rest );

            case 'R':
                return ParseParameter( reflectionName, assembly, rest );

            default:
                throw new FormatException( $"The prefix of '{reflectionName}' is not known." );
        }
    }

    private static V1ReflectionName ParseType( string reflectionName, string assembly, string typePath )
    {
        var type = TypePath.Parse( reflectionName, typePath );

        var declaringTypeName = type.Segments.Count > 1
            ? "T:[" + assembly + "]" + typePath.Substring( 0, type.LastSeparator )
            : null;

        return new V1ReflectionName( assembly, "T:" + type.Id, IntrospectionDeclarationKind.Type, type.Name, type.Namespace, declaringTypeName, null );
    }

    private static V1ReflectionName ParseMember( string reflectionName, char prefix, string assembly, string rest )
    {
        var separator = rest.IndexOf( "::", StringComparison.Ordinal );

        if ( separator < 0 )
        {
            throw new FormatException( $"'{reflectionName}' has no member name." );
        }

        var typePath = rest.Substring( 0, separator );
        var type = TypePath.Parse( reflectionName, typePath );
        var member = rest.Substring( separator + 2 );

        // The return type of a conversion operator, after ~ (op_Implicit(System.Int32)~App.C), as in a documentation comment
        // identifier.
        string? returnType = null;
        var tilde = member.LastIndexOf( ")~", StringComparison.Ordinal );

        if ( tilde >= 0 )
        {
            returnType = member.Substring( tilde + 2 );
            member = member.Substring( 0, tilde + 1 );
        }

        // The parameters, in the parentheses that end the member.
        string? parameters = null;

        if ( member.EndsWith( ")", StringComparison.Ordinal ) )
        {
            var open = FindOpening( member, member.Length - 1, '(', ')' );

            if ( open < 0 )
            {
                throw new FormatException( $"The parameters of '{reflectionName}' are not opened." );
            }

            parameters = member.Substring( open + 1, member.Length - open - 2 );
            member = member.Substring( 0, open );
        }

        // The generic parameters of a method, in braces at the end of its name. The braces of a name such as
        // {Name}k__BackingField are not at its end.
        var methodGenericParameters = new List<string>();

        if ( member.EndsWith( "}", StringComparison.Ordinal ) )
        {
            var open = FindOpening( member, member.Length - 1, '{', '}' );

            if ( open > 0 )
            {
                methodGenericParameters.AddRange( SplitTopLevel( member.Substring( open + 1, member.Length - open - 2 ) ) );
                member = member.Substring( 0, open );
            }
        }

        var kind = prefix switch
        {
            'M' => member is "#ctor" or "#cctor" ? IntrospectionDeclarationKind.Constructor : IntrospectionDeclarationKind.Method,
            'P' => IntrospectionDeclarationKind.Property,
            'F' => IntrospectionDeclarationKind.Field,
            _ => IntrospectionDeclarationKind.Event
        };

        var id = new StringBuilder();
        id.Append( prefix ).Append( ':' ).Append( type.Id ).Append( '.' ).Append( member );

        if ( methodGenericParameters.Count > 0 )
        {
            id.Append( "``" ).Append( methodGenericParameters.Count );
        }

        // A documentation comment identifier has no parentheses when there is no parameter.
        if ( !string.IsNullOrWhiteSpace( parameters ) )
        {
            id.Append( '(' );

            var first = true;

            foreach ( var parameter in SplitTopLevel( parameters! ) )
            {
                if ( !first )
                {
                    id.Append( ',' );
                }

                first = false;
                id.Append( ConvertParameterType( parameter, type.GenericParameters, methodGenericParameters ) );
            }

            id.Append( ')' );
        }

        if ( returnType != null )
        {
            id.Append( '~' ).Append( ConvertParameterType( returnType, type.GenericParameters, methodGenericParameters ) );
        }

        return new V1ReflectionName( assembly, id.ToString(), kind, member, null, "T:[" + assembly + "]" + typePath, null );
    }

    private static V1ReflectionName ParseParameter( string reflectionName, string assembly, string rest )
    {
        var at = rest.LastIndexOf( '@' );

        // The parameters end the signature of the method, before the return type of a conversion operator: op_Implicit(App.C)~System.Int32.
        var signature = at < 0 ? "" : rest.Substring( 0, at );
        var tilde = signature.LastIndexOf( ")~", StringComparison.Ordinal );
        var parameters = tilde >= 0 ? signature.Substring( 0, tilde + 1 ) : signature;

        if ( at < 0 || !parameters.EndsWith( ")", StringComparison.Ordinal ) )
        {
            throw new FormatException( $"'{reflectionName}' has no parameter name." );
        }

        var methodName = "M:[" + assembly + "]" + rest.Substring( 0, at );
        var method = ParseMember( reflectionName, 'M', assembly, rest.Substring( 0, at ) );
        var name = rest.Substring( at + 1 );

        return name == "return"
            ? new V1ReflectionName( assembly, method.Id + ";Return", IntrospectionDeclarationKind.ReturnParameter, name, null, null, methodName )
            : new V1ReflectionName( assembly, method.Id + ";Parameter=" + name, IntrospectionDeclarationKind.Parameter, name, null, null, methodName );
    }

    // Converts a parameter type: no assembly, no space, the generic parameters by position, a period for a nested type, @ for
    // a reference, and the bounds of the dimensions of an array.
    private static string ConvertParameterType( string type, IReadOnlyList<string> typeGenericParameters, IReadOnlyList<string> methodGenericParameters )
    {
        type = AssemblyQualifierRegex().Replace( type, "" ).Replace( " ", "", StringComparison.Ordinal );

        type = NameRegex().Replace(
            type,
            m =>
            {
                // A method generic parameter hides a type generic parameter of the same name.
                var methodIndex = LastIndexOf( methodGenericParameters, m.Value );

                if ( methodIndex >= 0 )
                {
                    return "``" + methodIndex;
                }

                // The generic parameters of the levels of a type, outermost first: a nested type that reuses the name of a parameter
                // of an outer type hides it, so the last one wins.
                var typeIndex = LastIndexOf( typeGenericParameters, m.Value );

                return typeIndex >= 0 ? "`" + typeIndex : m.Value;
            } );

        type = ArrayRankRegex().Replace( type, m => "[" + string.Join( ",", new string[m.Length - 1].Select( _ => "0:" ) ) + "]" );

        return type.Replace( '+', '.' ).Replace( '&', '@' );

        static int LastIndexOf( IReadOnlyList<string> list, string value )
        {
            for ( var i = list.Count - 1; i >= 0; i-- )
            {
                if ( list[i] == value )
                {
                    return i;
                }
            }

            return -1;
        }
    }

    // The position of the bracket that opens the one at the given position, or -1.
    private static int FindOpening( string text, int closing, char open, char close )
    {
        var depth = 0;

        for ( var i = closing; i >= 0; i-- )
        {
            if ( text[i] == close )
            {
                depth++;
            }
            else if ( text[i] == open && --depth == 0 )
            {
                return i;
            }
        }

        return -1;
    }

    // Splits at the commas that are not in braces, brackets or parentheses, and trims the parts.
    private static List<string> SplitTopLevel( string text )
    {
        var parts = new List<string>();
        var depth = 0;
        var start = 0;

        for ( var i = 0; i < text.Length; i++ )
        {
            switch ( text[i] )
            {
                case '{' or '[' or '(' or '<':
                    depth++;

                    break;

                case '}' or ']' or ')' or '>':
                    depth--;

                    break;

                case ',' when depth == 0:
                    parts.Add( text.Substring( start, i - start ).Trim() );
                    start = i + 1;

                    break;
            }
        }

        var last = text.Substring( start ).Trim();

        if ( last.Length > 0 || parts.Count > 0 )
        {
            parts.Add( last );
        }

        return parts;
    }

    /// <summary>
    /// The path of a type: its namespace and its nesting, with the generic parameters of each level.
    /// </summary>
    private sealed class TypePath
    {
        private TypePath( string id, string name, string? namespaceName, List<string> segments, List<string> genericParameters, int lastSeparator )
        {
            this.Id = id;
            this.Name = name;
            this.Namespace = namespaceName;
            this.Segments = segments;
            this.GenericParameters = genericParameters;
            this.LastSeparator = lastSeparator;
        }

        // The identifier without the prefix, such as App.Outer`1.Inner.
        public string Id { get; }

        public string Name { get; }

        public string? Namespace { get; }

        public IReadOnlyList<string> Segments { get; }

        // The generic parameters of all the levels, outermost first, as a documentation comment identifier numbers them.
        public IReadOnlyList<string> GenericParameters { get; }

        // The position of the last + of the path, which separates the declaring type of a nested type.
        public int LastSeparator { get; }

        public static TypePath Parse( string reflectionName, string path )
        {
            var segments = new List<string>();
            var genericParameters = new List<string>();
            var id = new StringBuilder();
            var depth = 0;
            var start = 0;
            var lastSeparator = -1;
            var name = "";
            string? namespaceName = null;

            for ( var i = 0; i <= path.Length; i++ )
            {
                if ( i < path.Length )
                {
                    if ( path[i] == '{' )
                    {
                        depth++;
                    }
                    else if ( path[i] == '}' )
                    {
                        depth--;
                    }

                    if ( path[i] != '+' || depth != 0 )
                    {
                        continue;
                    }

                    lastSeparator = i;
                }

                var segment = path.Substring( start, i - start );
                start = i + 1;

                if ( segment.Length == 0 )
                {
                    throw new FormatException( $"The type of '{reflectionName}' has an empty name." );
                }

                var arity = 0;

                if ( segment.EndsWith( "}", StringComparison.Ordinal ) )
                {
                    var open = segment.IndexOf( '{', StringComparison.Ordinal );
                    var parameters = SplitTopLevel( segment.Substring( open + 1, segment.Length - open - 2 ) );
                    genericParameters.AddRange( parameters );
                    arity = parameters.Count;
                    segment = segment.Substring( 0, open );
                }

                if ( segments.Count == 0 )
                {
                    var dot = segment.LastIndexOf( '.' );
                    namespaceName = dot > 0 ? segment.Substring( 0, dot ) : null;
                    name = dot > 0 ? segment.Substring( dot + 1 ) : segment;
                }
                else
                {
                    id.Append( '.' );
                    name = segment;
                }

                segments.Add( segment );
                id.Append( segment );

                if ( arity > 0 )
                {
                    id.Append( '`' ).Append( arity );
                }
            }

            return new TypePath( id.ToString(), name, namespaceName, segments, genericParameters, lastSeparator );
        }
    }
}
