// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace SharpCrafters.Backstage.FileLocks
{
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
            RetryWarning? warning = null,
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
                warning,
                budget );

        /// <summary>
        /// Executes a <see cref="Func{TResult}"/> and retries it upon failure.
        /// </summary>
        /// <param name="action">The operation.</param>
        /// <param name="retryPredicate">
        /// Determines whether an exception is retried. By default, an <see cref="IOException"/> or an
        /// <see cref="UnauthorizedAccessException"/> is.
        /// </param>
        /// <param name="logger">A logger that receives a warning at each retry.</param>
        /// <param name="onException">Called with the exception of the first failed attempt.</param>
        /// <param name="warning">Tells the user, once, that the operation keeps failing.</param>
        /// <param name="budget">How long to keep trying. By default, a dozen attempts.</param>
        /// <returns>The return value of <paramref name="action"/>.</returns>
        public static T Retry<T>(
            Func<T> action,
            Predicate<Exception>? retryPredicate = null,
            ILogger? logger = null,
            Action<Exception>? onException = null,
            RetryWarning? warning = null,
            RetryBudget? budget = null )
            => RetryCore(
                action,
                retryPredicate,
                logger,
                onException,
                warning?.CreateReporter( () => null ),
                budget );

        [ExcludeFromCodeCoverage]
        private static T RetryCore<T>(
            Func<T> action,
            Predicate<Exception>? retryPredicate,
            ILogger? logger,
            Action<Exception>? onException,
            Action<int, TimeSpan, Exception>? onFailedAttempt,
            RetryBudget? budget )
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

                    onFailedAttempt?.Invoke( i + 1, elapsed, e );

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
        public static void RetryWithLockDetection(
            string file,
            Action<string> action,
            IServiceProvider? serviceProvider,
            Predicate<Exception>? retryPredicate = null,
            ILogger? logger = null,
            RetryWarning? warning = null,
            RetryBudget? budget = null )
            => RetryWithLockDetection( new[] { file }, action, serviceProvider, retryPredicate, logger, warning, budget );

        /// <summary>
        /// Executes an action that affects a several files while retrying and reporting blocking processes upon lock.
        /// The action is executed once for each file and receives the file name as an argument.
        /// </summary>
        /// <remarks>
        /// The <paramref name="warning"/> is reported at most once for the whole call, not once for each file.
        /// </remarks>
        public static void RetryWithLockDetection(
            IReadOnlyList<string> files,
            Action<string> action,
            IServiceProvider? serviceProvider,
            Predicate<Exception>? retryPredicate = null,
            ILogger? logger = null,
            RetryWarning? warning = null,
            RetryBudget? budget = null )
        {
            var context = new DeadlockDetectionContext( serviceProvider, logger, files, warning );

            ExecuteWithLockDetection(
                () =>
                {
                    foreach ( var file in files )
                    {
                        RetryCore(
                            () =>
                            {
                                action( file );

                                return true;
                            },
                            retryPredicate,
                            logger,
                            context.OnRecoverableException,
                            context.OnFailedAttempt,
                            budget );
                    }

                    return true;
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
            IServiceProvider? serviceProvider,
            Predicate<Exception>? retryPredicate = null,
            ILogger? logger = null,
            RetryWarning? warning = null,
            RetryBudget? budget = null )
            => RetryWithLockDetection(
                files,
                () =>
                {
                    action();

                    return true;
                },
                serviceProvider,
                retryPredicate,
                logger,
                warning,
                budget );

        /// <summary>
        /// Executes a function that affects a several files while retrying and reporting blocking processes upon lock.
        /// The function is executed only once.
        /// </summary>
        /// <param name="files">The files that the function affects, whose holding processes are reported.</param>
        /// <param name="action">The operation.</param>
        /// <param name="serviceProvider">
        /// The services that provide the <see cref="ILockingProcessDetector"/>, or <c>null</c> in a process that started no
        /// services. Without the detector, the holding processes are not named.
        /// </param>
        /// <param name="retryPredicate">
        /// Determines whether an exception is retried. By default, an <see cref="IOException"/> or an
        /// <see cref="UnauthorizedAccessException"/> is.
        /// </param>
        /// <param name="logger">A logger that receives a warning at each retry, and the holding processes.</param>
        /// <param name="warning">Tells the user, once, that the operation keeps failing, naming the holding processes.</param>
        /// <param name="budget">How long to keep trying. By default, a dozen attempts.</param>
        /// <returns>The return value of <paramref name="action"/>.</returns>
        /// <exception cref="LockedFileException">
        /// The operation still failed when the budget was spent, and processes holding the files were found.
        /// </exception>
        public static T RetryWithLockDetection<T>(
            IReadOnlyList<string> files,
            Func<T> action,
            IServiceProvider? serviceProvider,
            Predicate<Exception>? retryPredicate = null,
            ILogger? logger = null,
            RetryWarning? warning = null,
            RetryBudget? budget = null )
        {
            var context = new DeadlockDetectionContext( serviceProvider, logger, files, warning );

            return ExecuteWithLockDetection(
                () => RetryCore( action, retryPredicate, logger, context.OnRecoverableException, context.OnFailedAttempt, budget ),
                context );
        }

        private static T ExecuteWithLockDetection<T>(
            Func<T> action,
            DeadlockDetectionContext context )
        {
            try
            {
                return action();
            }
            catch ( Exception e )
            {
                context.OnFatalException( e );

                throw;
            }
        }
    }
}
