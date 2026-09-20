// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using JetBrains.Annotations;
using SharpCrafters.Backstage.Diagnostics;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading;

namespace SharpCrafters.Backstage.Utilities
{
    /// <summary>
    /// Reports an attempt that failed and is about to be retried, so that a caller can decide for itself when a
    /// wait has lasted long enough to be worth telling the user about.
    /// </summary>
    /// <param name="attempts">The number of attempts made so far, starting at one.</param>
    /// <param name="elapsed">The time since the first attempt started.</param>
    /// <param name="exception">The exception that failed the attempt.</param>
    /// <remarks>
    /// It is called on every retry, so a caller that reports on every call produces one message per attempt. A
    /// caller that wants one message reports on a threshold of its own, of either argument.
    /// </remarks>
    [PublicAPI]
    public delegate void RetryReporter( int attempts, TimeSpan elapsed, Exception exception );

    [PublicAPI]
    public static partial class RetryHelper
    {
        /// <summary>
        /// Executes an <see cref="Action"/> and retries it upon failure.
        /// </summary>
        public static void Retry(
            Action action,
            Predicate<Exception>? retryPredicate = null,
            ILogger? logger = null,
            Action<Exception>? onException = null,
            RetryReporter? onRetry = null,
            RetryBudget? budget = null )
            => Retry(
                () =>
                {
                    action();

                    return true;
                },
                retryPredicate,
                logger,
                onException,
                onRetry,
                budget );

        /// <summary>
        /// Executes a <see cref="Func{TResult}"/> and retries it upon failure.
        /// </summary>
        [ExcludeFromCodeCoverage]
        public static T Retry<T>(
            Func<T> action,
            Predicate<Exception>? retryPredicate = null,
            ILogger? logger = null,
            Action<Exception>? onException = null,
            RetryReporter? onRetry = null,
            RetryBudget? budget = null )
        {
            var delay = 10.0;
            budget ??= RetryBudget.Default;
            retryPredicate ??= e => e is UnauthorizedAccessException or IOException || (uint) e.HResult == 0x80070020;

            var startTime = DateTime.UtcNow;

            for ( var i = 0; /* nothing */; i++ )
            {
                try
                {
                    return action();
                }
                catch ( Exception e ) when ( budget.AllowsAnotherAttempt( i + 1, DateTime.UtcNow - startTime ) && retryPredicate( e ) )
                {
                    var elapsed = DateTime.UtcNow - startTime;

                    logger?.Warning?.Log( $"{nameof(RetryHelper)} caught {e.GetType().Name} '{e.Message}'. Retrying in {delay}." );

                    if ( i == 0 )
                    {
                        onException?.Invoke( e );
                    }

                    onRetry?.Invoke( i + 1, elapsed, e );

                    var wait = budget.CapDelay( TimeSpan.FromMilliseconds( delay ), elapsed );

                    if ( wait > TimeSpan.Zero )
                    {
                        Thread.Sleep( wait );
                    }

                    delay *= 1.2;
                }
            }
        }

        /// <summary>
        /// Executes an action that affects a single file while retrying and reporting blocking processes upon lock.
        /// </summary>
        /// <param name="onFilesLocked">
        /// Called once, the first time the operation fails, with the names of the processes holding the files,
        /// for a product whose diagnostics are not a log. It is not called per attempt, so a caller that turns
        /// it into a message for the user does not produce one per retry.
        /// </param>
        public static void RetryWithLockDetection(
            string file,
            Action<string> action,
            IServiceProvider serviceProvider,
            Predicate<Exception>? retryPredicate = null,
            ILogger? logger = null,
            Action<string>? onFilesLocked = null,
            RetryReporter? onRetry = null,
            RetryBudget? budget = null )
            => RetryWithLockDetection( new[] { file }, action, serviceProvider, retryPredicate, logger, onFilesLocked, onRetry, budget );

        /// <summary>
        /// Executes an action that affects a several files while retrying and reporting blocking processes upon lock.
        /// The action is executed once for each file and receives the file name as an argument. 
        /// </summary>
        public static void RetryWithLockDetection(
            IReadOnlyList<string> files,
            Action<string> action,
            IServiceProvider serviceProvider,
            Predicate<Exception>? retryPredicate = null,
            ILogger? logger = null,
            Action<string>? onFilesLocked = null,
            RetryReporter? onRetry = null,
            RetryBudget? budget = null )
        {
            var context = new DeadlockDetectionContext( serviceProvider, logger, files, onFilesLocked );

            ExecuteWithLockDetection(
                () =>
                {
                    foreach ( var file in files )
                    {
                        Retry( () => action( file ), retryPredicate, logger, context.OnRecoverableException, onRetry, budget );
                    }
                },
                context );
        }

        /// <summary>
        /// Executes an action that affects a several files while retrying and reporting blocking processes upon lock.
        /// The action is executed only once.
        /// </summary>
        public static void RetryWithLockDetection(
            IReadOnlyList<string> files,
            Action action,
            IServiceProvider serviceProvider,
            Predicate<Exception>? retryPredicate = null,
            ILogger? logger = null,
            Action<string>? onFilesLocked = null,
            RetryReporter? onRetry = null,
            RetryBudget? budget = null )
        {
            var context = new DeadlockDetectionContext( serviceProvider, logger, files, onFilesLocked );

            ExecuteWithLockDetection( () => Retry( action, retryPredicate, logger, context.OnRecoverableException, onRetry, budget ), context );
        }

        private static void ExecuteWithLockDetection(
            Action action,
            DeadlockDetectionContext context )
        {
            try
            {
                action();
            }
            catch ( Exception e )
            {
                context.OnFatalException( e );

                throw;
            }
        }
    }
}