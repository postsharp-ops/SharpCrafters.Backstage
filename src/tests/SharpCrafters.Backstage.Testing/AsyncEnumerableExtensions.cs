// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using JetBrains.Annotations;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SharpCrafters.Backstage.Testing;

/// <summary>
/// Helpers that let a test assert on an asynchronous sequence with the synchronous assertions of the test framework.
/// </summary>
/// <remarks>
/// The names are not <c>ToListAsync</c> and <c>SingleAsync</c>, although that is what they do, because .NET 10 ships
/// <c>System.Linq.AsyncEnumerable</c> with members of exactly those names and shapes: a test that imported both
/// namespaces would then not compile on that target framework while compiling on the others. These names exist on
/// every target framework of this package and need no conditional compilation at the call site.
/// </remarks>
[PublicAPI]
public static class AsyncEnumerableExtensions
{
    /// <summary>
    /// Drains an asynchronous sequence into a list.
    /// </summary>
    public static async Task<List<T>> DrainAsync<T>( this IAsyncEnumerable<T> source, CancellationToken cancellationToken = default )
    {
        var list = new List<T>();

        await foreach ( var item in source.WithCancellation( cancellationToken ) )
        {
            list.Add( item );
        }

        return list;
    }

    /// <summary>
    /// Returns the single element of an asynchronous sequence, and fails when the sequence does not have exactly one.
    /// </summary>
    public static async Task<T> DrainSingleAsync<T>( this IAsyncEnumerable<T> source, CancellationToken cancellationToken = default )
    {
        var list = await source.DrainAsync( cancellationToken );

        return list.Count == 1
            ? list[0]
            : throw new InvalidOperationException( $"The sequence contains {list.Count} elements instead of one." );
    }
}
