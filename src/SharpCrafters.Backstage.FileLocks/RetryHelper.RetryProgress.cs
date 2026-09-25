// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

namespace SharpCrafters.Backstage.FileLocks;

public static partial class RetryHelper
{
    /// <summary>
    /// The progress of one call of a public method of <see cref="RetryHelper"/>: the time since its first attempt and
    /// the number of attempts that have failed. A call that retries one operation for each file shares one instance
    /// between the operations.
    /// </summary>
    private sealed class RetryProgress
    {
        private readonly IRetryClock _clock;

        /// <summary>
        /// Initializes a new instance of the <see cref="RetryProgress"/> class.
        /// </summary>
        /// <param name="clock">A clock created for this call.</param>
        public RetryProgress( IRetryClock clock )
        {
            this._clock = clock;
        }

        /// <summary>
        /// Gets the time since the first attempt of the call started.
        /// </summary>
        public TimeSpan Elapsed => this._clock.Elapsed;

        /// <summary>
        /// Blocks the current thread for the given time before the next attempt.
        /// </summary>
        public void Sleep( TimeSpan duration ) => this._clock.Sleep( duration );

        /// <summary>
        /// Gets or sets the number of attempts of the call that have failed so far.
        /// </summary>
        public int FailedAttempts { get; set; }
    }
}
