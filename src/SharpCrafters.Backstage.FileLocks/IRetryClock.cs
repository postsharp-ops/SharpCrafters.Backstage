// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using System.Diagnostics;

namespace SharpCrafters.Backstage.FileLocks;

/// <summary>
/// Measures the time of one call of a <see cref="RetryHelper"/> method, and waits between its attempts. Each call
/// creates its own instance, which starts measuring when it is created.
/// </summary>
/// <remarks>
/// The production implementation is <see cref="StopwatchRetryClock"/>. A test implements this interface with a clock
/// that advances by the requested time without waiting, so that a test of a time limit is exact and immediate.
/// </remarks>
internal interface IRetryClock
{
    /// <summary>
    /// Gets the time since the clock was created.
    /// </summary>
    TimeSpan Elapsed { get; }

    /// <summary>
    /// Waits for the given time.
    /// </summary>
    void Sleep( TimeSpan duration );
}

/// <summary>
/// The <see cref="IRetryClock"/> that measures the time with a <see cref="Stopwatch"/> and waits with
/// <see cref="Thread.Sleep(TimeSpan)"/>.
/// </summary>
internal sealed class StopwatchRetryClock : IRetryClock
{
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();

    public TimeSpan Elapsed => this._stopwatch.Elapsed;

    public void Sleep( TimeSpan duration ) => Thread.Sleep( duration );
}
