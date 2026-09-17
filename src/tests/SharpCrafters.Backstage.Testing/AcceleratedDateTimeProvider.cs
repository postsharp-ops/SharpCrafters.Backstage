// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using JetBrains.Annotations;
using SharpCrafters.Backstage.Infrastructure;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;

namespace SharpCrafters.Backstage.Testing;

/// <summary>
/// A clock that follows the accelerated clock of a license server, so that a simulation can live through weeks of
/// leases, renewals and expiries in minutes of real time.
/// </summary>
/// <remarks>
/// <para>
/// This lives in the test package and not in the product, as it does in PostSharp: a released product always reads
/// the real clock, and only a harness opts into an accelerated one. The server side of the protocol is equally
/// conditional: a license server honours its acceleration factor in debug builds only.
/// </para>
/// <para>
/// The protocol is one request. <c>GET {url}/GetTime.ashx</c> answers <c>{utcNow};{acceleration}</c>, where the
/// instant is the current reading of the server's own virtual clock and the factor is how much faster than real time
/// that clock runs. The client anchors its virtual clock on the reading and then advances it at the same rate:
/// </para>
/// <code>
/// virtualNow = serverTimeAtSync + (realNow - realTimeAtSync) * acceleration
/// </code>
/// <para>
/// The round trip is not compensated, exactly as in the original: at an acceleration of 1440 a round trip of 50 ms is
/// already 72 virtual seconds of drift. The remedy is to <see cref="SyncAsync"/> again whenever the drift shows,
/// which for a licensing harness means whenever a lease arrives whose renewal instant has already passed.
/// </para>
/// </remarks>
[PublicAPI]
public sealed class AcceleratedDateTimeProvider : IDateTimeProvider
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string _timeUrl;
    private readonly object _sync = new();

    private DateTime _serverTimeAtSync;
    private long _timestampAtSync;
    private decimal _acceleration;

    /// <summary>
    /// Initializes a new instance of the <see cref="AcceleratedDateTimeProvider"/> class. The clock reads real time
    /// until <see cref="SyncAsync"/> has been called.
    /// </summary>
    /// <param name="httpClientFactory">The factory of the client that contacts the server.</param>
    /// <param name="licenseServerUrl">The base URL of the license server, without the path of the time endpoint.</param>
    public AcceleratedDateTimeProvider( IHttpClientFactory httpClientFactory, string licenseServerUrl )
    {
        this._httpClientFactory = httpClientFactory;

        // The base URL is trimmed and the path appended here, rather than concatenated at the call site, because the
        // original harness concatenated the two without a separator for this endpoint while trimming for the other,
        // so a URL without a trailing slash broke the clock alone and left the leases working.
        this._timeUrl = licenseServerUrl.TrimEnd( '/' ) + LicenseServerSimulator.TimePath;

        this._serverTimeAtSync = DateTime.UtcNow;
        this._timestampAtSync = Stopwatch.GetTimestamp();
        this._acceleration = 1;
    }

    /// <summary>
    /// Gets the acceleration factor that the server last reported. It is 1 until <see cref="SyncAsync"/> has been
    /// called, that is, the clock runs at real speed.
    /// </summary>
    public decimal Acceleration
    {
        get
        {
            lock ( this._sync )
            {
                return this._acceleration;
            }
        }
    }

    /// <summary>
    /// Gets the number of times the clock has been synchronized with the server, which a test asserts to verify that
    /// drift triggers a new synchronization.
    /// </summary>
    public int SyncCount { get; private set; }

    /// <summary>
    /// Reads the clock of the server and anchors the virtual clock on it.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <exception cref="InvalidOperationException">The server did not answer with an instant and a factor.</exception>
    public async Task SyncAsync( CancellationToken cancellationToken = default )
    {
        using var client = this._httpClientFactory.Create();

#if NET6_0_OR_GREATER
        var body = await client.GetStringAsync( this._timeUrl, cancellationToken );
#else
        _ = cancellationToken;
        var body = await client.GetStringAsync( this._timeUrl );
#endif

        var parts = body.Split( ';' );

        if ( parts.Length != 2 )
        {
            throw new InvalidOperationException(
                $"The license server at '{this._timeUrl}' answered '{body}', which is not an instant followed by an acceleration factor." );
        }

        DateTime serverTime;
        decimal acceleration;

        try
        {
            serverTime = XmlConvert.ToDateTime( parts[0].Trim(), XmlDateTimeSerializationMode.Utc );
            acceleration = XmlConvert.ToDecimal( parts[1].Trim() );
        }
        catch ( Exception e ) when ( e is FormatException or ArgumentException or OverflowException )
        {
            throw new InvalidOperationException( $"The license server at '{this._timeUrl}' answered '{body}', which does not parse.", e );
        }

        lock ( this._sync )
        {
            this._serverTimeAtSync = serverTime;
            this._timestampAtSync = Stopwatch.GetTimestamp();

            // A server that reports no acceleration, or an acceleration of one, runs at real speed. Zero is treated
            // as one rather than as a stopped clock, because that is what the server means by it.
            this._acceleration = acceleration == 0 ? 1 : acceleration;
            this.SyncCount++;
        }

        this.DateChanged?.Invoke();
    }

    /// <inheritdoc />
    public DateTime UtcNow
    {
        get
        {
            lock ( this._sync )
            {
                return this._serverTimeAtSync + this.GetVirtualElapsed();
            }
        }
    }

    /// <summary>
    /// Blocks until the virtual clock reaches an instant, which is how a simulated user waits for the next moment at
    /// which it would run a build.
    /// </summary>
    /// <param name="virtualTime">The instant of the virtual clock to wait for.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <remarks>
    /// An instant already past returns at once. The wait is computed in real time, so it is the virtual delay divided
    /// by the acceleration factor.
    /// </remarks>
    public async Task WaitUntilAsync( DateTime virtualTime, CancellationToken cancellationToken = default )
    {
        TimeSpan realDelay;

        lock ( this._sync )
        {
            var virtualDelay = virtualTime.ToUniversalTime() - (this._serverTimeAtSync + this.GetVirtualElapsed());

            if ( virtualDelay <= TimeSpan.Zero )
            {
                return;
            }

            // The acceleration is never zero here: the constructor sets one and SyncAsync maps zero onto one. The
            // original divided by a factor initialized to zero, so a call before the first synchronization produced an
            // infinite delay and threw.
            realDelay = TimeSpan.FromTicks( (long) (virtualDelay.Ticks / (double) this._acceleration) );
        }

        await Task.Delay( realDelay, cancellationToken );
    }

    /// <summary>
    /// Converts the real time elapsed since the last synchronization into virtual time.
    /// </summary>
    /// <remarks>
    /// The elapsed time is measured with <see cref="Stopwatch"/> and not by subtracting two readings of
    /// <see cref="DateTime.UtcNow"/>, so that an adjustment of the clock of the machine, by a time service or by a
    /// change of daylight saving, does not move the virtual clock by that amount multiplied by the acceleration.
    /// </remarks>
    private TimeSpan GetVirtualElapsed()
    {
        var realElapsedTicks = (Stopwatch.GetTimestamp() - this._timestampAtSync) * (double) TimeSpan.TicksPerSecond / Stopwatch.Frequency;

        return TimeSpan.FromTicks( (long) (realElapsedTicks * (double) this._acceleration) );
    }

    /// <inheritdoc />
    public event Action? DateChanged;

    /// <inheritdoc />
    public void Dispose() { }

    public override string ToString() => $"Accelerated clock of '{this._timeUrl}', acceleration {this.Acceleration}";
}
