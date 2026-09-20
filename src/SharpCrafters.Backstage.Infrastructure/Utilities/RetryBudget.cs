// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using JetBrains.Annotations;
using System;

namespace SharpCrafters.Backstage.Utilities;

/// <summary>
/// How long <see cref="RetryHelper"/> keeps trying: a number of attempts, a total time, or both.
/// </summary>
/// <remarks>
/// <para>
/// The default is a dozen attempts over a fraction of a second, which suits an operation that fails because
/// something else is finishing, such as a file a virus scanner has open for a moment.
/// </para>
/// <para>
/// It does not suit every caller. A compiler writing its output waits for the file to be released by an
/// application the developer is running, which takes as long as it takes to close it, and its user is told to
/// raise that wait when it is not enough. Such a caller asks for a duration and gets as many attempts as fit.
/// </para>
/// </remarks>
[PublicAPI]
public sealed class RetryBudget
{
    /// <summary>
    /// A dozen attempts, which is what callers that ask for nothing get.
    /// </summary>
    public static RetryBudget Default { get; } = new( 12, null );

    /// <summary>
    /// Keeps trying for a number of attempts, however long they take.
    /// </summary>
    public static RetryBudget OfAttempts( int maxAttempts ) => new( maxAttempts, null );

    /// <summary>
    /// Keeps trying until a total time has passed, however many attempts fit into it. The wait between attempts
    /// never overshoots the deadline.
    /// </summary>
    public static RetryBudget OfDuration( TimeSpan duration ) => new( null, duration );

    private RetryBudget( int? maxAttempts, TimeSpan? duration )
    {
        this.MaxAttempts = maxAttempts;
        this.Duration = duration;
    }

    /// <summary>
    /// Gets the maximal number of attempts, or <see langword="null"/> when only the time is limited.
    /// </summary>
    public int? MaxAttempts { get; }

    /// <summary>
    /// Gets the maximal total time, or <see langword="null"/> when only the number of attempts is limited.
    /// </summary>
    public TimeSpan? Duration { get; }

    /// <summary>
    /// Determines whether another attempt is allowed.
    /// </summary>
    /// <param name="attemptsMade">The number of attempts that have failed so far.</param>
    /// <param name="elapsed">The time since the first attempt started.</param>
    internal bool AllowsAnotherAttempt( int attemptsMade, TimeSpan elapsed )
        => (this.MaxAttempts == null || attemptsMade < this.MaxAttempts.Value)
           && (this.Duration == null || elapsed < this.Duration.Value);

    /// <summary>
    /// Returns the time to wait before the next attempt, shortened so that the wait does not outlast the budget.
    /// </summary>
    internal TimeSpan CapDelay( TimeSpan delay, TimeSpan elapsed )
    {
        if ( this.Duration == null )
        {
            return delay;
        }

        var remaining = this.Duration.Value - elapsed;

        return remaining < delay ? remaining : delay;
    }
}
