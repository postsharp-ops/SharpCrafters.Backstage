// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

namespace SharpCrafters.Aspects.Introspection.Query.Output;

/// <summary>
/// How much of an object is written.
/// </summary>
internal enum JsonDetail
{
    // The fields marked as summary, for an object inside another one.
    Summary,

    // Every field but the lists of objects, which list writes.
    Full,

    // Every field, and the lists of objects as summary rows, which show writes.
    Expanded,

    // Every field, and the lists of objects with their own fields but their lists, which show declaration writes for the aspect
    // instances of the declaration.
    ExpandedFull
}
