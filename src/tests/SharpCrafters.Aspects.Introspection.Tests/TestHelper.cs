// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using System;
using System.IO;
using System.Text.RegularExpressions;

namespace SharpCrafters.Aspects.Introspection.Tests;

/// <summary>
/// The helpers of the tests of both schema versions.
/// </summary>
internal static class TestHelper
{
    /// <summary>
    /// Gets the directory of the fixtures, which the build copies next to the tests.
    /// </summary>
    public static string FixturesDirectory => Path.Combine( AppContext.BaseDirectory, "Fixtures" );

    /// <summary>
    /// Converts the line endings to LF, so that texts whose line endings depend on the checkout and on the writer can be compared.
    /// </summary>
    public static string ToLf( this string text ) => Regex.Replace( text, "\r\n", "\n" );
}
