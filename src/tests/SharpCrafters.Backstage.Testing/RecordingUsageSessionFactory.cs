// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using JetBrains.Annotations;
using SharpCrafters.Backstage.Telemetry;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace SharpCrafters.Backstage.Testing;

/// <summary>
/// A usage session that <see cref="RecordingUsageSessionFactory"/> created.
/// </summary>
/// <param name="Kind">The kind of the session.</param>
/// <param name="ShouldCollectMetrics">A value indicating whether the session was asked to collect metrics.</param>
[PublicAPI]
public sealed record RecordedUsageSession( string Kind, bool ShouldCollectMetrics );

/// <summary>
/// A test usage session factory that records each session it creates and returns a session that reports nothing.
/// </summary>
/// <remarks>
/// A telemetry context calls the factory only when its policy allows usage telemetry. A test therefore asserts that
/// <see cref="Sessions"/> is empty to prove that a disabled policy tracks no usage.
/// </remarks>
[PublicAPI]
public sealed class RecordingUsageSessionFactory : IUsageSessionFactory
{
    private readonly ConcurrentQueue<RecordedUsageSession> _sessions = new();

    /// <summary>
    /// Gets the sessions that were created, in the order of creation.
    /// </summary>
    public IReadOnlyCollection<RecordedUsageSession> Sessions => this._sessions;

    public IUsageSession CreateSession( string kind, bool shouldCollectMetrics = true )
    {
        this._sessions.Enqueue( new RecordedUsageSession( kind, shouldCollectMetrics ) );

        return NullUsageSession.Instance;
    }
}
