// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

namespace SharpCrafters.Aspects.Introspection.Query.Querying;

/// <summary>
/// The kind of the value of a field.
/// </summary>
internal enum ValueKind
{
    String,
    Integer,
    Boolean,

    // A string among the names of an enumeration.
    Enum,

    // An object of another type of the schema.
    Object,

    // A list of objects of another type of the schema.
    ObjectList,

    StringList
}
