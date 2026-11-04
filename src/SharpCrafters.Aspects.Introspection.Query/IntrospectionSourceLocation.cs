// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

namespace SharpCrafters.Aspects.Introspection.Query;

/// <summary>
/// A position in a source file.
/// </summary>
/// <param name="File">The full path of the file.</param>
/// <param name="Line">The line, from 1.</param>
/// <param name="Column">The column, from 1.</param>
public sealed record IntrospectionSourceLocation( string File, int Line, int Column );
