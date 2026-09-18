// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using JetBrains.Annotations;
using SharpCrafters.Backstage.Diagnostics;
using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace SharpCrafters.Backstage.Utilities
{
    public static partial class RetryHelper
    {
        /// <summary>
        /// Executes an asynchronous operation and retries it upon failure, with the same schedule and the same
        /// default retry predicate as <see cref="Retry{T}"/>.
        /// </summary>
        /// <remarks>
        /// The waits between the attempts are asynchronous, so a caller on a thread that must not block, or a caller
        /// that has given a cancellation token, is not held on a blocking sleep.
        /// </remarks>
        [PublicAPI]
        [ExcludeFromCodeCoverage]
        public static async Task RetryAsync(
            Func<Task> operation,
            Predicate<Exception>? retryPredicate = null,
            ILogger? logger = null,
            CancellationToken cancellationToken = default )
        {
            var delay = 10.0;
            const int maxAttempts = 12;
            retryPredicate ??= e => e is UnauthorizedAccessException or IOException || (uint) e.HResult == 0x80070020;

            for ( var i = 0; /* nothing */; i++ )
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    await operation();

                    return;
                }

                // A cancellation is never retried: the caller has asked to stop, and the exception that reports it is
                // an IOException on some platforms, which the default predicate would otherwise accept.
                catch ( Exception e ) when ( i < maxAttempts && e is not OperationCanceledException && retryPredicate( e ) )
                {
                    logger?.Warning?.Log( $"{nameof(RetryHelper)} caught {e.GetType().Name} '{e.Message}'. Retrying in {delay}." );

                    await Task.Delay( TimeSpan.FromMilliseconds( delay ), cancellationToken );
                    delay *= 1.2;
                }
            }
        }
    }
}
