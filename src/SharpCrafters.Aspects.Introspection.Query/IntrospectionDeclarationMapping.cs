// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

namespace SharpCrafters.Aspects.Introspection.Query;

/// <summary>
/// The replacement of a declaration of the source code by a declaration of the woven assembly, such as a field encapsulated as
/// a property (PostSharp).
/// </summary>
public sealed class IntrospectionDeclarationMapping
{
    internal IntrospectionDeclarationMapping( IntrospectionDeclaration source, IntrospectionDeclaration target, DeclarationMappingKind? kind )
    {
        this.Source = source;
        this.Target = target;
        this.Kind = kind;
    }

    /// <summary>
    /// Gets the source declaration.
    /// </summary>
    public IntrospectionDeclaration Source { get; }

    /// <summary>
    /// Gets the target declaration.
    /// </summary>
    public IntrospectionDeclaration Target { get; }

    /// <summary>
    /// Gets the kind of the mapping, or <c>null</c> when the file does not give it.
    /// </summary>
    public DeclarationMappingKind? Kind { get; }
}
