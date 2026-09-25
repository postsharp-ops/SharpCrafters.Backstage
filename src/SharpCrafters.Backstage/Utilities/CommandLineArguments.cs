// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using System.Collections.Generic;
using System.Text;

namespace SharpCrafters.Backstage.Utilities;

internal static class CommandLineArguments
{
    private static readonly char[] _charactersRequiringQuoting = { ' ', '\t', '\n', '\v', '"' };

    /// <summary>
    /// Joins an argument vector into a single command-line string, quoting each argument as required so that it
    /// round-trips through the Windows <c>CommandLineToArgvW</c> parsing rules. This prevents untrusted argument
    /// values from injecting additional arguments. Equivalent to the .NET <c>PasteArguments</c> implementation,
    /// which we cannot use directly because it is internal and because <c>ProcessStartInfo.ArgumentList</c>
    /// is not available on all target frameworks.
    /// </summary>
    /// <remarks>
    /// The runtime splits <c>ProcessStartInfo.Arguments</c> with the same rules on Linux and macOS, so the string is
    /// valid on every operating system.
    /// </remarks>
    public static string Format( IReadOnlyList<string> arguments )
    {
        var builder = new StringBuilder();

        foreach ( var argument in arguments )
        {
            AppendArgument( builder, argument );
        }

        return builder.ToString();
    }

    private static void AppendArgument( StringBuilder builder, string argument )
    {
        if ( builder.Length != 0 )
        {
            builder.Append( ' ' );
        }

        // An argument with no whitespace or quote can be appended verbatim.
        if ( argument.Length != 0 && argument.IndexOfAny( _charactersRequiringQuoting ) < 0 )
        {
            builder.Append( argument );

            return;
        }

        builder.Append( '"' );

        var index = 0;

        while ( index < argument.Length )
        {
            var c = argument[index++];

            if ( c == '\\' )
            {
                var backslashCount = 1;

                while ( index < argument.Length && argument[index] == '\\' )
                {
                    index++;
                    backslashCount++;
                }

                if ( index == argument.Length )
                {
                    // Backslashes immediately preceding the closing quote must be doubled.
                    builder.Append( '\\', backslashCount * 2 );
                }
                else if ( argument[index] == '"' )
                {
                    // Backslashes preceding a quote must be doubled, plus one to escape the quote itself.
                    builder.Append( '\\', ( backslashCount * 2 ) + 1 );
                    builder.Append( '"' );
                    index++;
                }
                else
                {
                    builder.Append( '\\', backslashCount );
                }
            }
            else if ( c == '"' )
            {
                builder.Append( '\\' );
                builder.Append( '"' );
            }
            else
            {
                builder.Append( c );
            }
        }

        builder.Append( '"' );
    }
}
