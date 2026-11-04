// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;

namespace SharpCrafters.Backstage.Utilities;

internal static class EnumerableExtensions
{
    /// <summary>
    /// Invokes an action for each item, and continues after an item throws, so that one item that fails does not
    /// prevent the action on the others. The first exception is rethrown after the last item.
    /// </summary>
    /// <remarks>
    /// The items are processed one after the other, never one inside the other.
    /// </remarks>
    public static void ForEachContinueOnException<T>( this IEnumerable<T> items, Action<T> action )
    {
        Exception? firstException = null;

        foreach ( var item in items )
        {
            try
            {
                action( item );
            }
            catch ( Exception e )
            {
                firstException ??= e;
            }
        }

        if ( firstException != null )
        {
            ExceptionDispatchInfo.Capture( firstException ).Throw();
        }
    }
}
