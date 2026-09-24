// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

namespace SharpCrafters.Backstage.FileLocks;

/// <summary>
/// Describes the failures of a retried operation when a <see cref="RetryWarning"/> is reported.
/// </summary>
public sealed class RetryWarningContext
{
    internal RetryWarningContext( int attempts, TimeSpan elapsed, Exception exception, string? lockingProcesses )
    {
        this.Attempts = attempts;
        this.Elapsed = elapsed;
        this.Exception = exception;
        this.LockingProcesses = lockingProcesses;
    }

    /// <summary>
    /// Gets the number of attempts that have failed so far.
    /// </summary>
    public int Attempts { get; }

    /// <summary>
    /// Gets the time since the first attempt started.
    /// </summary>
    public TimeSpan Elapsed { get; }

    /// <summary>
    /// Gets the exception that failed the last attempt.
    /// </summary>
    public Exception Exception { get; }

    /// <summary>
    /// Gets a sentence that names the processes holding the files, or <c>null</c>.
    /// </summary>
    /// <remarks>
    /// It is <c>null</c> when the operation was not started by a <c>RetryWithLockDetection</c> method, when the service
    /// provider has no <see cref="ILockingProcessDetector"/>, and when no process holding the files was found.
    /// </remarks>
    public string? LockingProcesses { get; }
}
