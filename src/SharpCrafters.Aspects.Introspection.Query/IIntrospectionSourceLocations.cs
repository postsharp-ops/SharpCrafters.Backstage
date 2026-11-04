// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using System.Collections.Generic;

namespace SharpCrafters.Aspects.Introspection.Query;

/// <summary>
/// The source locations of the declarations of a target, which a product records apart from its file, such as the
/// <c>.pspdb</c> of PostSharp.
/// </summary>
public interface IIntrospectionSourceLocations
{
    /// <summary>
    /// Gets the location of a declaration, or <c>null</c>.
    /// </summary>
    IntrospectionSourceLocation? GetLocation( IntrospectionDeclaration declaration );

    /// <summary>
    /// Gets the documentation comment identifiers of the declarations whose source includes a line of a file, the innermost
    /// first.
    /// </summary>
    IReadOnlyList<string> FindDeclarations( string file, int line );
}
