// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

namespace SharpCrafters.Aspects.Introspection;

/// <summary>
/// The kind of a declaration mapping (<see cref="DeclarationMappingData.Kind"/>), the <c>Kind</c> attribute of an <c>Evolution</c>
/// element of version 1.
/// </summary>
public enum DeclarationMappingKind
{
    /// <summary>
    /// The source declaration was renamed into the target declaration, such as a field into its original field.
    /// </summary>
    Renamed,

    /// <summary>
    /// The target declaration wraps the source declaration, such as a property that encapsulates a field.
    /// </summary>
    Wrapped,

    /// <summary>
    /// The compiler generated the target declaration for the source declaration, such as the <c>MoveNext</c> method of the state
    /// machine of an async method.
    /// </summary>
    CompilerGenerated
}
