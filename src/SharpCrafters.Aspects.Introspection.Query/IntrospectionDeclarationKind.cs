// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

namespace SharpCrafters.Aspects.Introspection.Query;

/// <summary>
/// The kind of a declaration (<see cref="IntrospectionDeclaration.Kind"/>).
/// </summary>
public enum IntrospectionDeclarationKind
{
    /// <summary>
    /// An assembly.
    /// </summary>
    Assembly,

    /// <summary>
    /// A type.
    /// </summary>
    Type,

    /// <summary>
    /// A method that is not a constructor, including an accessor and an operator.
    /// </summary>
    Method,

    /// <summary>
    /// An instance or static constructor.
    /// </summary>
    Constructor,

    /// <summary>
    /// A property, including an indexer.
    /// </summary>
    Property,

    /// <summary>
    /// A field.
    /// </summary>
    Field,

    /// <summary>
    /// An event.
    /// </summary>
    Event,

    /// <summary>
    /// A parameter of a method.
    /// </summary>
    Parameter,

    /// <summary>
    /// The return parameter of a method.
    /// </summary>
    ReturnParameter
}
