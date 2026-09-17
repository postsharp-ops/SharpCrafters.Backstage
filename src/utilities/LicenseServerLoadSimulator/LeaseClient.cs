// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Infrastructure;
using System.Globalization;
using System.Net;
using System.Text;
using System.Xml;

namespace SharpCrafters.Backstage.LicenseServerLoadSimulator;

/// <summary>
/// The lease held by one simulated user.
/// </summary>
internal sealed record LicenseLeaseInfo( string LicenseKey, DateTime StartTime, DateTime EndTime, DateTime RenewTime );

/// <summary>
/// What asking the server for a lease produced.
/// </summary>
internal sealed record LeaseResult( LicenseLeaseInfo? Lease, string? ErrorMessage );

/// <summary>
/// Asks a license server for a lease, on behalf of a simulated user rather than of the current one.
/// </summary>
/// <remarks>
/// <para>
/// This does not use the client of the product, because that one takes the user and the machine from the operating
/// system and stores the lease in the configuration of the current user: one process can therefore hold one lease,
/// whereas a simulation is an organization of many users on many machines.
/// </para>
/// <para>
/// It does build exactly the same request, because the point of the simulation is to load a real server the way the
/// product loads it. A change to the request of the product that is not made here makes the simulation meaningless,
/// which is why the contract is pinned by a unit test rather than only here.
/// </para>
/// </remarks>
internal sealed class LeaseClient
{
    private const int _maxResponseLength = 64 * 1024;

    private readonly SimulationOptions _options;
    private readonly HttpClient _httpClient;

    public LeaseClient( SimulationOptions options, IHttpClientFactory httpClientFactory )
    {
        this._options = options;

        this._httpClient = httpClientFactory.Create(
            new HttpClientOptions { UseDefaultCredentials = true, Timeout = TimeSpan.FromSeconds( 30 ) } );
    }

    public async Task<LeaseResult> TryGetLeaseAsync( string userName, string machine, CancellationToken cancellationToken )
    {
        var requestUri = this.GetRequestUri( userName, machine );

        HttpStatusCode statusCode;
        string body;

        try
        {
            using var response = await this._httpClient.GetAsync( requestUri, cancellationToken ).ConfigureAwait( false );
            statusCode = response.StatusCode;
            body = await ReadBoundedAsync( response.Content ).ConfigureAwait( false );
        }
        catch ( OperationCanceledException ) when ( cancellationToken.IsCancellationRequested )
        {
            throw;
        }
        catch ( OperationCanceledException )
        {
            return new LeaseResult( null, "The server did not answer in time." );
        }
        catch ( Exception e )
        {
            return new LeaseResult( null, e.Message );
        }

        if ( statusCode != HttpStatusCode.OK )
        {
            // The body of a 403 is the explanation the administrator of the server configured, and of a 503 the
            // message that its global lock timed out under the load this simulation is producing.
            return new LeaseResult( null, $"HTTP {(int) statusCode}: {body.Trim()}" );
        }

        return TryParse( body, out var lease ) ? new LeaseResult( lease, null ) : new LeaseResult( null, $"Unparsable response: {body.Trim()}" );
    }

    /// <summary>
    /// Builds the request, which has to be the one the product sends: the arguments, their order, and the machine name
    /// followed by a hyphen and a hexadecimal hash, which a server strips before it matches a build server.
    /// </summary>
    private string GetRequestUri( string userName, string machine )
    {
        var builder = new StringBuilder( this._options.Url.TrimEnd( '/' ) );
        builder.Append( "/Lease.ashx?user=" ).Append( Uri.EscapeDataString( userName ) );
        builder.Append( "&machine=" ).Append( Uri.EscapeDataString( machine ) );
        builder.Append( "&version=" ).Append( Uri.EscapeDataString( this._options.Version ) );

        builder.Append( "&buildDate=" )
            .Append( Uri.EscapeDataString( new DateTime( 2026, 1, 15, 0, 0, 0, DateTimeKind.Utc ).ToString( "o", CultureInfo.InvariantCulture ) ) );

        if ( this._options.Product != null )
        {
            builder.Append( "&product=" ).Append( Uri.EscapeDataString( this._options.Product ) );
        }

        return builder.ToString();
    }

    private static bool TryParse( string body, out LicenseLeaseInfo? lease )
    {
        lease = null;

        string? licenseKey = null;
        DateTime? startTime = null, endTime = null, renewTime = null;

        try
        {
            foreach ( var part in body.Split( ';' ) )
            {
                var separator = part.IndexOf( ':', StringComparison.Ordinal );

                if ( separator < 0 || separator == part.Length - 1 )
                {
                    continue;
                }

                var name = part.Substring( 0, separator ).Trim();
                var value = part.Substring( separator + 1 ).Trim();

                switch ( name.ToLowerInvariant() )
                {
                    case "license":
                        licenseKey = value;

                        break;

                    case "starttime":
                        startTime = XmlConvert.ToDateTime( value, XmlDateTimeSerializationMode.Utc );

                        break;

                    case "endtime":
                        endTime = XmlConvert.ToDateTime( value, XmlDateTimeSerializationMode.Utc );

                        break;

                    case "renewtime":
                        renewTime = XmlConvert.ToDateTime( value, XmlDateTimeSerializationMode.Utc );

                        break;
                }
            }
        }
        catch ( Exception e ) when ( e is FormatException or ArgumentException or OverflowException )
        {
            return false;
        }

        if ( string.IsNullOrEmpty( licenseKey ) )
        {
            return false;
        }

        var start = startTime ?? DateTime.UtcNow;
        var end = endTime ?? start.AddDays( 1 );
        lease = new LicenseLeaseInfo( licenseKey!, start, end, renewTime ?? end );

        return true;
    }

    private static async Task<string> ReadBoundedAsync( HttpContent content )
    {
        if ( content.Headers.ContentLength > _maxResponseLength )
        {
            return "";
        }

        var body = await content.ReadAsStringAsync().ConfigureAwait( false );

        return body.Length > _maxResponseLength ? body.Substring( 0, _maxResponseLength ) : body;
    }
}
