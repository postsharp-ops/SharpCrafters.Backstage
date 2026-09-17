// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using JetBrains.Annotations;
using SharpCrafters.Backstage.Infrastructure;
using SharpCrafters.Common;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;

namespace SharpCrafters.Backstage.Testing;

/// <summary>
/// An in-process stand-in for a license server. It plugs into <see cref="TestHttpClientFactory"/>, so it uses no
/// socket and no port, answers the lease and time endpoints, records what the client asked, accounts seats the way a
/// real server does, and can be told to misbehave in each of the ways a real one does.
/// </summary>
/// <remarks>
/// <para>
/// Everything is derived from the clock of the test, so <c>Time.AddTime( … )</c> is what drives expiry and renewal and
/// nothing ever waits.
/// </para>
/// <para>
/// Several simulators can run in one test as long as their <see cref="Url"/> differ: each registers its own hook, and
/// the hooks of two different URLs never both match a request.
/// </para>
/// <para>
/// The body of a lease is written here by hand rather than by calling the parser's counterpart in the product,
/// because the format is an external contract: a serializer of our own would agree with our parser whatever the real
/// servers emit, which is exactly the weakness of the test double this class replaces.
/// </para>
/// </remarks>
[PublicAPI]
public sealed class LicenseServerSimulator
{
    /// <summary>
    /// The URL used when the test does not choose one. It is <c>https</c> and carries no query string, so it is a
    /// well-formed license server URL.
    /// </summary>
    public const string DefaultUrl = "https://license.test";

    /// <summary>
    /// The path of the endpoint that leases a licence.
    /// </summary>
    public const string LeasePath = "/Lease.ashx";

    /// <summary>
    /// The path of the endpoint that reports the clock of the server.
    /// </summary>
    public const string TimePath = "/GetTime.ashx";

    /// <summary>
    /// The name of the synchronization point that the simulator reaches inside the request handler, before it builds
    /// its answer. A concurrency test enables it on <see cref="SynchronizationProvider"/> to hold one caller inside
    /// the request while it drives another.
    /// </summary>
    public const string LeaseSyncPointName = "LicenseServerSimulator.Lease";

    private readonly IDateTimeProvider _time;
    private readonly Uri _leaseUri;
    private readonly Uri _timeUri;
    private readonly object _sync = new();
    private readonly List<LeaseRequest> _requests = new();

    /// <summary>
    /// The machines on which each user currently holds a lease, and the instant at which each of those leases ends.
    /// A real server counts usage per user and not per machine, so the state is shaped the same way.
    /// </summary>
    private readonly Dictionary<string, Dictionary<string, DateTime>> _leasesByUser =
        new( StringComparer.OrdinalIgnoreCase );

    private DateTime? _graceStartTime;

    /// <summary>
    /// Initializes a new instance of the <see cref="LicenseServerSimulator"/> class and registers its hooks on the
    /// HTTP client factory of the test. Every later change of a property takes effect on the next request.
    /// </summary>
    /// <param name="httpClientFactory">The HTTP client factory of the test, on which the hooks are registered.</param>
    /// <param name="time">The clock of the test, from which the lease instants are derived.</param>
    /// <param name="url">The base URL of the server. The default is <see cref="DefaultUrl"/>.</param>
    public LicenseServerSimulator( TestHttpClientFactory httpClientFactory, IDateTimeProvider time, string url = DefaultUrl )
    {
        this._time = time;
        this.Url = url.TrimEnd( '/' );
        this.LeaseUrl = this.Url + LeasePath;
        this.TimeUrl = this.Url + TimePath;
        this._leaseUri = new Uri( this.LeaseUrl, UriKind.Absolute );
        this._timeUri = new Uri( this.TimeUrl, UriKind.Absolute );

        httpClientFactory.InsertHook( this.Matches, this.HandleAsync );
    }

    /// <summary>
    /// Gets the base URL of the server, without a trailing slash and without a query string. This is the string that a
    /// test registers as a licence.
    /// </summary>
    public string Url { get; }

    /// <summary>
    /// Gets the URL of the lease endpoint, that is, <see cref="Url"/> followed by <see cref="LeasePath"/>.
    /// </summary>
    public string LeaseUrl { get; }

    /// <summary>
    /// Gets the URL of the time endpoint, that is, <see cref="Url"/> followed by <see cref="TimePath"/>.
    /// </summary>
    public string TimeUrl { get; }

    /// <summary>
    /// Gets or sets a value indicating whether the server answers at all. A disabled server lets the request fall
    /// through to the hooks registered before it.
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the licence key that the server leases. Changing it between two requests is how a test makes the
    /// server hand out a different key on renewal, and so proves that the renewed lease is the one adopted.
    /// </summary>
    public string LicenseKey { get; set; } = "";

    /// <summary>
    /// Gets or sets a delegate that chooses the licence key per request, which takes precedence over
    /// <see cref="LicenseKey"/>. Used where one server leases different keys to different users.
    /// </summary>
    public Func<LeaseRequest, string>? LicenseKeySelector { get; set; }

    /// <summary>
    /// Gets or sets the lifetime of a lease. The default is three days, which is the default of a real server.
    /// </summary>
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromDays( 3 );

    /// <summary>
    /// Gets or sets the period after which the client should renew. The default is one day less than
    /// <see cref="LeaseDuration"/>, which is how a real server computes it.
    /// </summary>
    public TimeSpan? RenewPeriod { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the answer carries the start instant. Set it to <see langword="false"/>
    /// to exercise the default of the parser.
    /// </summary>
    public bool IncludesStartTime { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether the answer carries the end instant.
    /// </summary>
    public bool IncludesEndTime { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether the answer carries the renewal instant.
    /// </summary>
    public bool IncludesRenewTime { get; set; } = true;

    /// <summary>
    /// Gets or sets parts appended to the answer, which the parser has to ignore, as it must ignore the parts that a
    /// server of a later version adds.
    /// </summary>
    public string? ExtraResponseParts { get; set; }

    /// <summary>
    /// Gets or sets the number of seats, or <see langword="null"/> for an unlimited number. Usage is counted per user,
    /// as the sum over the users of the number of machines each holds divided by <see cref="MachinesPerUser"/> and
    /// rounded up.
    /// </summary>
    public int? MaxSeats { get; set; }

    /// <summary>
    /// Gets or sets the number of machines that one user may hold within one seat. The default is two, which is the
    /// default of a real server.
    /// </summary>
    public int MachinesPerUser { get; set; } = 2;

    /// <summary>
    /// Gets or sets the percentage of seats granted beyond <see cref="MaxSeats"/> during the grace period. The default
    /// is zero, so that a test of the seat limit gets a denial rather than a grace lease; a real licence carries 30 by
    /// default, and a test of the grace period sets it.
    /// </summary>
    public int GracePercent { get; set; }

    /// <summary>
    /// Gets or sets the number of days for which the grace period lasts once it has begun, which is on the first
    /// request that exceeds <see cref="MaxSeats"/>. The default is zero, so there is no grace period.
    /// </summary>
    public int GraceDays { get; set; }

    /// <summary>
    /// Gets or sets the machine names that the server treats as build servers. Their leases are not stored and consume
    /// no seat. The comparison ignores the hash suffix of the machine argument, as a real server does.
    /// </summary>
    public ImmutableHashSet<string> BuildServerMachines { get; set; } =
        ImmutableHashSet<string>.Empty.WithComparer( StringComparer.OrdinalIgnoreCase );

    /// <summary>
    /// Gets or sets the body of the denial, which a real server uses to explain to the user why no lease was granted.
    /// The default names the number of seats.
    /// </summary>
    public string? DenialMessage { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the server answers the time endpoint. A server that does not is what a
    /// deployment older than the clock protocol looks like.
    /// </summary>
    public bool ServesTime { get; set; } = true;

    /// <summary>
    /// Gets or sets the acceleration factor that the time endpoint reports. The default is the default of a real
    /// server: one real minute is one virtual day.
    /// </summary>
    public decimal Acceleration { get; set; } = 1440;

    /// <summary>
    /// Gets or sets the abnormal behaviour of the server.
    /// </summary>
    public LicenseServerFault FaultMode { get; set; }

    /// <summary>
    /// Gets or sets the number of requests that are answered normally before <see cref="FaultMode"/> starts to apply,
    /// so that a test can let the first acquisition succeed and make the renewal fail.
    /// </summary>
    public int FaultAfterRequestCount { get; set; }

    /// <summary>
    /// Gets or sets the body served by <see cref="LicenseServerFault.GarbageResponse"/>.
    /// </summary>
    public string GarbageBody { get; set; } =
        "<html><head><title>Sign in</title></head><body>Proxy authentication required.</body></html>";

    /// <summary>
    /// Gets or sets the delay of <see cref="LicenseServerFault.Slow"/>. It honours the cancellation token of the
    /// request, so a client that applies a timeout abandons it.
    /// </summary>
    public TimeSpan ResponseDelay { get; set; } = TimeSpan.FromSeconds( 30 );

    /// <summary>
    /// Gets or sets a delegate that composes the whole answer, bypassing every other property. It is the escape hatch
    /// for a case that does not deserve a property of its own.
    /// </summary>
    public Func<LeaseRequest, HttpResponseMessage>? ResponseFactory { get; set; }

    /// <summary>
    /// Gets or sets the synchronization provider on which <see cref="LeaseSyncPointName"/> is reached, or
    /// <see langword="null"/> when the test drives no interleaving.
    /// </summary>
    public ITestSynchronizationProvider? SynchronizationProvider { get; set; }

    /// <summary>
    /// Gets the requests received since the last call to <see cref="ClearRequests"/>, in the order in which the server
    /// started to handle them.
    /// </summary>
    public IReadOnlyList<LeaseRequest> Requests
    {
        get
        {
            lock ( this._sync )
            {
                return this._requests.ToImmutableArray();
            }
        }
    }

    /// <summary>
    /// Gets the number of requests received since the last call to <see cref="ClearRequests"/>.
    /// </summary>
    public int RequestCount
    {
        get
        {
            lock ( this._sync )
            {
                return this._requests.Count;
            }
        }
    }

    /// <summary>
    /// Gets the last request received, or <see langword="null"/> when the server was not contacted.
    /// </summary>
    public LeaseRequest? LastRequest
    {
        get
        {
            lock ( this._sync )
            {
                return this._requests.Count == 0 ? null : this._requests[this._requests.Count - 1];
            }
        }
    }

    /// <summary>
    /// Gets the machines on which each user currently holds a lease, whether or not those leases have ended.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyCollection<string>> Seats
    {
        get
        {
            lock ( this._sync )
            {
                return this._leasesByUser.ToDictionary(
                    user => user.Key,
                    user => (IReadOnlyCollection<string>) user.Value.Keys.ToImmutableArray(),
                    StringComparer.OrdinalIgnoreCase );
            }
        }
    }

    /// <summary>
    /// Gets the number of seats currently occupied, counting only the leases that have not ended according to the
    /// clock of the test.
    /// </summary>
    public int OccupiedSeatCount
    {
        get
        {
            lock ( this._sync )
            {
                this.ExpireLeases();

                return this.GetUsage();
            }
        }
    }

    /// <summary>
    /// Forgets the requests received so far. The seats are not released.
    /// </summary>
    public void ClearRequests()
    {
        lock ( this._sync )
        {
            this._requests.Clear();
        }
    }

    /// <summary>
    /// Releases every seat, as an administrator who cancels every lease does.
    /// </summary>
    public void ReleaseAllSeats()
    {
        lock ( this._sync )
        {
            this._leasesByUser.Clear();
            this._graceStartTime = null;
        }
    }

    /// <summary>
    /// Asserts that the server was not contacted since the last call to <see cref="ClearRequests"/>. This is the
    /// assertion of a test that a valid lease costs no request.
    /// </summary>
    public void AssertNotContacted()
    {
        var requests = this.Requests;

        if ( requests.Count > 0 )
        {
            throw new InvalidOperationException(
                $"The license server at '{this.Url}' was expected not to be contacted, but it received {requests.Count} request(s): "
                + string.Join( ", ", requests.Select( r => r.RequestUri.ToString() ) ) );
        }
    }

    /// <summary>
    /// Asserts the number of requests received since the last call to <see cref="ClearRequests"/>.
    /// </summary>
    /// <param name="expectedCount">The expected number of requests.</param>
    public void AssertContacted( int expectedCount = 1 )
    {
        var requests = this.Requests;

        if ( requests.Count != expectedCount )
        {
            throw new InvalidOperationException(
                $"The license server at '{this.Url}' was expected to receive {expectedCount} request(s) but received {requests.Count}: "
                + string.Join( ", ", requests.Select( r => r.RequestUri.ToString() ) ) );
        }
    }

    /// <summary>
    /// Formats the body of a lease, without serving it. A test of the parser uses it to obtain a well-formed body
    /// without going through HTTP.
    /// </summary>
    public string FormatLease( string licenseKey, DateTime startTime, DateTime endTime, DateTime renewTime )
    {
        var builder = new StringBuilder();
        builder.Append( "License: " ).Append( licenseKey );

        if ( this.IncludesStartTime )
        {
            builder.Append( "; StartTime: " ).Append( XmlConvert.ToString( startTime, XmlDateTimeSerializationMode.Utc ) );
        }

        if ( this.IncludesEndTime )
        {
            builder.Append( "; EndTime: " ).Append( XmlConvert.ToString( endTime, XmlDateTimeSerializationMode.Utc ) );
        }

        if ( this.IncludesRenewTime )
        {
            builder.Append( "; RenewTime: " ).Append( XmlConvert.ToString( renewTime, XmlDateTimeSerializationMode.Utc ) );
        }

        if ( this.ExtraResponseParts != null )
        {
            builder.Append( "; " ).Append( this.ExtraResponseParts );
        }

        return builder.ToString();
    }

    /// <summary>
    /// Decides whether a request is addressed to this server. The comparison covers the scheme, the host, the port and
    /// the path, so that two simulators at two URLs never both match, and so that the query string, which carries what
    /// is under test, plays no part in the routing.
    /// </summary>
    private bool Matches( HttpRequestMessage request )
    {
        if ( !this.IsEnabled || request.RequestUri == null )
        {
            return false;
        }

        return Matches( request.RequestUri, this._leaseUri ) || (this.ServesTime && Matches( request.RequestUri, this._timeUri ));

        static bool Matches( Uri requestUri, Uri endpointUri )
            => Uri.Compare(
                   requestUri,
                   endpointUri,
                   UriComponents.SchemeAndServer | UriComponents.Path,
                   UriFormat.Unescaped,
                   StringComparison.OrdinalIgnoreCase )
               == 0;
    }

    private async Task<HttpResponseMessage> HandleAsync( HttpRequestMessage request, CancellationToken cancellationToken )
    {
        var requestUri = request.RequestUri!;

        if ( this.ServesTime
             && Uri.Compare( requestUri, this._timeUri, UriComponents.SchemeAndServer | UriComponents.Path, UriFormat.Unescaped, StringComparison.OrdinalIgnoreCase )
             == 0 )
        {
            return Text(
                HttpStatusCode.OK,
                XmlConvert.ToString( this._time.UtcNow, XmlDateTimeSerializationMode.Utc ) + ";" + XmlConvert.ToString( this.Acceleration ) );
        }

        var leaseRequest = ParseRequest( requestUri, this._time.UtcNow );

        lock ( this._sync )
        {
            this._requests.Add( leaseRequest );
        }

        if ( this.SynchronizationProvider != null )
        {
            await this.SynchronizationProvider.SyncPointAsync( LeaseSyncPointName, cancellationToken );
        }

        if ( this.ResponseFactory != null )
        {
            return this.ResponseFactory( leaseRequest );
        }

        var now = this._time.UtcNow;

        if ( this.RequestCount > this.FaultAfterRequestCount )
        {
            switch ( this.FaultMode )
            {
                case LicenseServerFault.Unreachable:
                    throw new HttpRequestException( $"No such host is known. ({this._leaseUri.Host}:{this._leaseUri.Port})" );

                case LicenseServerFault.Timeout:
                    // The exception that HttpClient raises when its own timeout elapses, so the client under test
                    // meets the same shape as in production without the test waiting.
                    throw new TaskCanceledException(
                        "The request was canceled due to the configured HttpClient.Timeout of 10 seconds elapsing.",
                        new TimeoutException() );

                case LicenseServerFault.BadRequest:
                    return Text( HttpStatusCode.BadRequest, "Missing query string argument: machine." );

                case LicenseServerFault.Forbidden:
                    return Text( HttpStatusCode.Forbidden, this.DenialMessage ?? this.GetDefaultDenialMessage() );

                case LicenseServerFault.NotFound:
                    return Text( HttpStatusCode.NotFound, "<html><body>404 - File or directory not found.</body></html>" );

                case LicenseServerFault.InternalServerError:
                    return Text( HttpStatusCode.InternalServerError, "<html><body>Server Error in '/' Application.</body></html>" );

                case LicenseServerFault.ServiceUnavailable:
                    return Text( HttpStatusCode.ServiceUnavailable, "Service overloaded." );

                case LicenseServerFault.GarbageResponse:
                    return Text( HttpStatusCode.OK, this.GarbageBody );

                case LicenseServerFault.EmptyResponse:
                    return Text( HttpStatusCode.OK, "" );

                case LicenseServerFault.MissingLicenseKey:
                    return Text(
                        HttpStatusCode.OK,
                        "StartTime: " + XmlConvert.ToString( now, XmlDateTimeSerializationMode.Utc )
                                      + "; EndTime: " + XmlConvert.ToString( now + this.LeaseDuration, XmlDateTimeSerializationMode.Utc ) );

                case LicenseServerFault.ExpiredLease:
                    return Text(
                        HttpStatusCode.OK,
                        this.FormatLease( this.GetLicenseKey( leaseRequest ), now - this.LeaseDuration, now - TimeSpan.FromMinutes( 1 ), now - TimeSpan.FromMinutes( 1 ) ) );

                case LicenseServerFault.Slow:
                    await Task.Delay( this.ResponseDelay, cancellationToken );

                    break;
            }
        }

        if ( !this.TryTakeSeat( leaseRequest, now, out var denialMessage ) )
        {
            return Text( HttpStatusCode.Forbidden, denialMessage );
        }

        var renewPeriod = this.RenewPeriod ?? (this.LeaseDuration - TimeSpan.FromDays( 1 ));

        return Text(
            HttpStatusCode.OK,
            this.FormatLease( this.GetLicenseKey( leaseRequest ), now, now + this.LeaseDuration, now + renewPeriod ) );
    }

    private string GetLicenseKey( LeaseRequest request ) => this.LicenseKeySelector?.Invoke( request ) ?? this.LicenseKey;

    private string GetDefaultDenialMessage()
        => "No license with free capacity. "
           + (this.MaxSeats == null
               ? "The license server refused to grant a lease."
               : $"All {this.MaxSeats} seats of this license server are currently in use.");

    /// <summary>
    /// Accounts the seat of a request, following the rules of a real server: a machine on which the user already holds
    /// a lease renews it and takes nothing; a user who already holds a number of machines that is not a multiple of
    /// <see cref="MachinesPerUser"/> gets the next one without a capacity check; otherwise the request takes a new
    /// seat if there is capacity, or a grace seat if the grace period allows one.
    /// </summary>
    private bool TryTakeSeat( LeaseRequest request, DateTime now, out string denialMessage )
    {
        denialMessage = "";

        lock ( this._sync )
        {
            this.ExpireLeases();

            var machine = request.Machine ?? "";

            // A build server never consumes a seat, and its lease is not stored, so that its builds cannot exhaust the
            // seats of the developers.
            if ( request.MachineName != null && this.BuildServerMachines.Contains( request.MachineName ) )
            {
                return true;
            }

            var user = request.User ?? "";

            if ( !this._leasesByUser.TryGetValue( user, out var machines ) )
            {
                machines = new Dictionary<string, DateTime>( StringComparer.OrdinalIgnoreCase );
            }

            // A renewal on a machine the user already holds.
            if ( machines.ContainsKey( machine ) )
            {
                machines[machine] = now + this.LeaseDuration;
                this._leasesByUser[user] = machines;

                return true;
            }

            // The rule of a real server is a modulo and not a comparison: a user holding a number of machines that is
            // not a multiple of MachinesPerUser gets the next one for free, because it fits in a seat already paid for.
            if ( machines.Count > 0 && machines.Count % this.MachinesPerUser != 0 )
            {
                machines[machine] = now + this.LeaseDuration;
                this._leasesByUser[user] = machines;

                return true;
            }

            if ( this.MaxSeats is { } maxSeats && this.GetUsage() >= maxSeats )
            {
                this._graceStartTime ??= now;

                var graceLimit = (int) Math.Ceiling( maxSeats * (100.0 + this.GracePercent) / 100.0 );
                var graceEnd = this._graceStartTime.Value.AddDays( this.GraceDays );

                if ( !(graceEnd > now && this.GetUsage() < graceLimit) )
                {
                    denialMessage = this.DenialMessage ?? this.GetDefaultDenialMessage();

                    return false;
                }
            }

            machines[machine] = now + this.LeaseDuration;
            this._leasesByUser[user] = machines;

            return true;
        }
    }

    /// <summary>
    /// Releases the leases that have ended. A real server stores no expiry job either: a lease is simply not counted
    /// once the instant of its end has passed.
    /// </summary>
    private void ExpireLeases()
    {
        var now = this._time.UtcNow;

        foreach ( var user in this._leasesByUser.Keys.ToList() )
        {
            var machines = this._leasesByUser[user];

            foreach ( var machine in machines.Where( m => m.Value <= now ).Select( m => m.Key ).ToList() )
            {
                machines.Remove( machine );
            }

            if ( machines.Count == 0 )
            {
                this._leasesByUser.Remove( user );
            }
        }
    }

    /// <summary>
    /// Counts the occupied seats, as a real server does: per user, the number of machines divided by
    /// <see cref="MachinesPerUser"/> and rounded up.
    /// </summary>
    private int GetUsage()
        => this._leasesByUser.Values.Sum( machines => (int) Math.Ceiling( machines.Count / (double) this.MachinesPerUser ) );

    private static HttpResponseMessage Text( HttpStatusCode statusCode, string body )
        => new( statusCode ) { Content = new StringContent( body, Encoding.UTF8, "text/plain" ) };

    /// <summary>
    /// Parses the query string by hand, because <c>System.Web.HttpUtility</c> is not available on every target
    /// framework of this package, and because a test must observe exactly what the client sent, including an argument
    /// that the client is not supposed to send.
    /// </summary>
    private static LeaseRequest ParseRequest( Uri uri, DateTime receivedAt )
    {
        var arguments = new Dictionary<string, string>( StringComparer.Ordinal );

        foreach ( var pair in uri.Query.TrimStart( '?' ).Split( new[] { '&' }, StringSplitOptions.RemoveEmptyEntries ) )
        {
            var separator = pair.IndexOf( "=", StringComparison.Ordinal );

            if ( separator > 0 )
            {
                arguments[Uri.UnescapeDataString( pair.Substring( 0, separator ) )] =
                    Uri.UnescapeDataString( pair.Substring( separator + 1 ) );
            }
        }

        string? Get( string name ) => arguments.TryGetValue( name, out var value ) ? value : null;

        return new LeaseRequest
        {
            RequestUri = uri,
            User = Get( "user" ),
            Machine = Get( "machine" ),
            Version = Get( "version" ),
            BuildDate = Get( "buildDate" ),
            Product = Get( "product" ),
            QueryArguments = arguments,
            ReceivedAt = receivedAt
        };
    }

    public override string ToString() => $"License server simulator at '{this.Url}'";

    /// <summary>
    /// Formats a number the way the query string of the client does, so that a test can compose the value it expects.
    /// </summary>
    internal static string FormatHash( long hash ) => hash.ToString( "x", CultureInfo.InvariantCulture );
}
