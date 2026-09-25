// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

namespace SharpCrafters.Backstage.FileLocks;

/// <summary>
/// Tells the user, once, that an operation retried by <see cref="RetryHelper"/> has kept failing past a threshold.
/// </summary>
/// <remarks>
/// <para>
/// A short wait is ordinary, for instance while a virus scanner has a file open, and is not worth a message. A caller
/// whose diagnostics are not a log, such as a compiler that reports through the messages of a build, gives a threshold
/// and a delegate that writes its own message.
/// </para>
/// <para>
/// The delegate is called at most once for each call of a <see cref="RetryHelper"/> method, at the first failed attempt
/// that reaches the threshold. This includes the last attempt that the budget allows, just before the exception
/// propagates. The delegate is not called when the operation succeeds before the threshold is reached.
/// </para>
/// <para>
/// The thresholds are compared with the number of failed attempts and the time of the whole call. When the method
/// retries one operation for each file, the attempts of all the files count.
/// </para>
/// </remarks>
public sealed class RetryWarning
{
    private readonly int? _attempts;
    private readonly TimeSpan? _duration;
    private readonly Action<RetryWarningContext> _report;

    private RetryWarning( int? attempts, TimeSpan? duration, Action<RetryWarningContext> report )
    {
        this._attempts = attempts;
        this._duration = duration;
        this._report = report ?? throw new ArgumentNullException( nameof(report) );
    }

    /// <summary>
    /// Creates a warning that is reported when a number of attempts have failed.
    /// </summary>
    /// <param name="attempts">The number of failed attempts from which the warning is reported.</param>
    /// <param name="report">Writes the warning.</param>
    public static RetryWarning AfterAttempts( int attempts, Action<RetryWarningContext> report ) => new( attempts, null, report );

    /// <summary>
    /// Creates a warning that is reported when the operation has been failing for a given time.
    /// </summary>
    /// <param name="duration">The time since the first attempt from which the warning is reported.</param>
    /// <param name="report">Writes the warning.</param>
    public static RetryWarning AfterDuration( TimeSpan duration, Action<RetryWarningContext> report ) => new( null, duration, report );

    /// <summary>
    /// Creates the delegate that one call of a <see cref="RetryHelper"/> method invokes after each failed attempt. The
    /// delegate reports the warning the first time the threshold is reached, and does nothing on later calls.
    /// </summary>
    /// <param name="getLockingProcesses">Returns the description of the processes holding the files, or <c>null</c>.</param>
    internal Action<int, TimeSpan, Exception> CreateReporter( Func<string?> getLockingProcesses )
    {
        var reported = false;

        return ( attempts, elapsed, exception ) =>
        {
            if ( reported || !this.IsDue( attempts, elapsed ) )
            {
                return;
            }

            reported = true;

            this._report( new RetryWarningContext( attempts, elapsed, exception, getLockingProcesses() ) );
        };
    }

    private bool IsDue( int attempts, TimeSpan elapsed )
        => (this._attempts != null && attempts >= this._attempts.Value)
           || (this._duration != null && elapsed >= this._duration.Value);
}
