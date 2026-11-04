// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using System;
using System.Collections.Generic;
using System.Text;

namespace SharpCrafters.Aspects.Introspection.Query.Commands;

/// <summary>
/// Splits a line of a batch into words, as a POSIX shell does: words are separated by whitespace, <c>'…'</c> is literal, and inside
/// <c>"…"</c>, a backslash escapes <c>"</c>, <c>\</c>, <c>$</c> and <c>`</c>. Outside quotes, a backslash escapes a whitespace, a
/// quote or a backslash, as in <c>My\ File.cs</c>; any other backslash is literal, so that a Windows path such as
/// <c>src\Customer.cs</c> needs no quotes. A quoted part can be joined to other text in the same word.
/// </summary>
internal static class QueryLineSplitter
{
    /// <exception cref="FormatException">A quote is not closed, or the line ends with an escaping backslash.</exception>
    public static string[] Split( string line )
    {
        var words = new List<string>();
        var word = new StringBuilder();
        var inWord = false;

        for ( var i = 0; i < line.Length; i++ )
        {
            var c = line[i];

            if ( char.IsWhiteSpace( c ) )
            {
                if ( inWord )
                {
                    words.Add( word.ToString() );
                    word.Clear();
                    inWord = false;
                }

                continue;
            }

            inWord = true;

            switch ( c )
            {
                case '\'':
                    var end = line.IndexOf( '\'', i + 1 );

                    if ( end < 0 )
                    {
                        throw new FormatException( $"The quote at column {i + 1} is not closed." );
                    }

                    word.Append( line, i + 1, end - i - 1 );
                    i = end;

                    break;

                case '"':
                    var start = i;

                    for ( i++; ; i++ )
                    {
                        if ( i >= line.Length )
                        {
                            throw new FormatException( $"The quote at column {start + 1} is not closed." );
                        }

                        if ( line[i] == '"' )
                        {
                            break;
                        }

                        if ( line[i] == '\\' && i + 1 < line.Length && line[i + 1] is '"' or '\\' or '$' or '`' )
                        {
                            i++;
                        }

                        word.Append( line[i] );
                    }

                    break;

                case '\\' when i + 1 == line.Length:
                    throw new FormatException( $"The backslash at column {i + 1} escapes nothing." );

                case '\\' when char.IsWhiteSpace( line[i + 1] ) || line[i + 1] is '\'' or '"' or '\\':
                    i++;
                    word.Append( line[i] );

                    break;

                default:
                    word.Append( c );

                    break;
            }
        }

        if ( inWord )
        {
            words.Add( word.ToString() );
        }

        return words.ToArray();
    }
}
