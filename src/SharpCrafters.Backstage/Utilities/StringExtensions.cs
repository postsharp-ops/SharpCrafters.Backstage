// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

#if NET5_0_OR_GREATER
using System;
#endif

namespace SharpCrafters.Backstage.Utilities;

internal static class StringExtensions
{
    public static bool ContainsOrdinal( this string? s, string substring )
#if NET5_0_OR_GREATER
        => s?.Contains( substring, StringComparison.Ordinal ) ?? false;
#else
        => s?.Contains( substring ) ?? false;
#endif

    /// <summary>
    /// Gets the position of the first occurrence of a character, compared by its value.
    /// </summary>
    /// <remarks>
    /// The overload of <see cref="string.IndexOf(char)"/> that takes a comparison was added in .NET 5 and does not
    /// exist on .NET Framework, where passing one binds to the overload taking a starting position instead and
    /// searches from whatever number the comparison happens to be. That compiles on the frameworks which have both,
    /// so the mistake shows only when the .NET Framework target is built.
    /// </remarks>
    public static int IndexOfOrdinal( this string s, char c )
#if NET5_0_OR_GREATER
        => s.IndexOf( c, StringComparison.Ordinal );
#else

        // The overload taking a character alone compares by value, which is what is wanted here; the analyzer asks
        // for a comparison because it cannot tell that apart from a search for text.
#pragma warning disable CA1307
        => s.IndexOf( c );
#pragma warning restore CA1307
#endif
}
