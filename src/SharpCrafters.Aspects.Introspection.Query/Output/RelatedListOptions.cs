// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

namespace SharpCrafters.Aspects.Introspection.Query.Output;

/// <summary>
/// How <c>show</c> writes the lists of the objects related to the object it shows: at most <see cref="Limit"/> objects per list, or
/// all of them when it is <c>null</c>.
/// </summary>
/// <remarks>
/// A list is written as <c>{ "count", "truncated", "items" }</c>, with <c>byNamespace</c> and <c>next</c> when it is truncated, so
/// that its shape does not depend on its length. An aspect class can have thousands of aspect instances, and an aspect instance of a
/// type-level aspect thousands of transformations: written in full, they make an output too large for an agent to read.
/// </remarks>
internal sealed record RelatedListOptions( int? Limit )
{
    /// <summary>
    /// The number of objects of a list that <c>show</c> writes by default.
    /// </summary>
    public const int DefaultLimit = 20;
}
