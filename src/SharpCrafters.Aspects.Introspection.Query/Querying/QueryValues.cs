// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using System.Collections.Generic;

namespace SharpCrafters.Aspects.Introspection.Query.Querying;

/// <summary>
/// The normalization and the comparison of the values of the queries.
/// </summary>
internal static class QueryValues
{
    /// <summary>
    /// Normalizes a value read from the model or a literal: integers are <see cref="long"/>, other numbers are
    /// <see cref="double"/>, and an empty string is <c>null</c>, as an absent attribute of the
    /// file.
    /// </summary>
    public static object? Normalize( object? value )
        => value switch
        {
            string { Length: 0 } => null,
            int i => (long) i,
            short s => (long) s,
            byte b => (long) b,
            sbyte b => (long) b,
            float f => (double) f,
            decimal d => (double) d,
            _ => value
        };

    public static bool AreEqual( object? x, object? y )
        => (x, y) switch
        {
            (long a, double b) => a == b,
            (double a, long b) => a == b,
            _ => object.Equals( x, y )
        };

    /// <summary>
    /// Compares two values of the same type: strings by ordinal, numbers, and <c>false</c> before <c>true</c>. <c>null</c> comes
    /// first.
    /// </summary>
    public static int Compare( object? x, object? y )
        => (x, y) switch
        {
            (null, null) => 0,
            (null, _) => -1,
            (_, null) => 1,
            (string a, string b) => string.CompareOrdinal( a, b ),
            (long a, double b) => ((double) a).CompareTo( b ),
            (double a, long b) => a.CompareTo( b ),
            _ => Comparer<object>.Default.Compare( x, y )
        };

    public static IComparer<object?> Comparer { get; } = Comparer<object?>.Create( Compare );
}
