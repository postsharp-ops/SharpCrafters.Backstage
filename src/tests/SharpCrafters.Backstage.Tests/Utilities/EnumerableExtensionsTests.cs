// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Utilities;
using System;
using System.Collections.Generic;
using Xunit;

namespace SharpCrafters.Backstage.Tests.Utilities;

/// <summary>
/// Tests <see cref="EnumerableExtensions.ForEachContinueOnException{T}"/>, which updates the products of a composite
/// product one after the other. A product that cannot be updated must not prevent the update of the others, and no
/// failure may be lost.
/// </summary>
public sealed class EnumerableExtensionsTests
{
    [Fact]
    public void EveryItemIsProcessedAndEveryExceptionIsReported()
    {
        var processed = new List<int>();
        var first = new InvalidOperationException( "first" );
        var second = new ArgumentException( "second" );

        var aggregate = Assert.Throws<AggregateException>(
            () => new[] { 1, 2, 3 }.ForEachContinueOnException(
                i =>
                {
                    processed.Add( i );

                    if ( i == 1 )
                    {
                        throw first;
                    }

                    if ( i == 3 )
                    {
                        throw second;
                    }
                } ) );

        Assert.Equal( [1, 2, 3], processed );
        Assert.Equal( [first, second], aggregate.InnerExceptions );
    }

    [Fact]
    public void NothingIsThrownWhenNoItemFails()
    {
        var processed = new List<int>();

        new[] { 1, 2 }.ForEachContinueOnException( processed.Add );

        Assert.Equal( [1, 2], processed );
    }
}
