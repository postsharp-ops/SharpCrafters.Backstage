// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

namespace SharpCrafters.Aspects.Introspection.Query.Querying;

/// <summary>
/// Extends <see cref="IntrospectionProvider"/> with the rules that the queries and the output apply to the data that a product
/// records.
/// </summary>
internal static class ProviderExtensions
{
    /// <summary>
    /// Gets a value indicating whether the files of a product provide a field. A field is not provided when the product says so,
    /// or when its value is an object or a list of objects of a collection that the product does not record, such as the
    /// annotations of a declaration for a product that records no annotation.
    /// </summary>
    public static bool Provides( this IntrospectionProvider provider, TypeDefinition type, FieldDefinition field )
        => provider.Provides( type.Name, field.Name ) && (field.TypeName == null || provider.Provides( field.TypeName, null ));
}
