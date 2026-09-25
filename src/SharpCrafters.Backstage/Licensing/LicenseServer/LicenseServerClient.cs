// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Application;
using SharpCrafters.Backstage.Diagnostics;
using SharpCrafters.Backstage.Extensibility;
using SharpCrafters.Backstage.Infrastructure;
using SharpCrafters.Backstage.Utilities;
using System;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SharpCrafters.Backstage.Licensing.LicenseServer;

/// <summary>
/// Obtains a lease from a license server, honouring the lease that is already stored.
/// </summary>
internal sealed class LicenseServerClient : IBackstageService
{
    /// <summary>
    /// The maximal size of a response, beyond which the server is not answering with a lease. A real lease is a few
    /// hundred bytes; the bound exists so that a broken or hostile server cannot make the client read without end.
    /// </summary>
    private const int _maxResponseLength = 64 * 1024;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly IMachineIdProvider _machineIdProvider;
    private readonly IUserIdentityProvider _userIdentityProvider;
    private readonly IApplicationInfo _applicationInfo;
    private readonly LicenseLeaseStore _leaseStore;
    private readonly ILogger _logger;

    /// <summary>
    /// The dispatcher on which a failed renewal is announced, or <see langword="null"/> in a service graph that has
    /// none. A command line has no user interface to notify, and must not fail for want of one.
    /// </summary>
    private readonly IEventDispatcher? _eventDispatcher;

    private readonly LicensingInitializationOptions _options;

    public LicenseServerClient( IServiceProvider serviceProvider, LicensingInitializationOptions options )
    {
        this._httpClientFactory = serviceProvider.GetRequiredBackstageService<IHttpClientFactory>();
        this._dateTimeProvider = serviceProvider.GetRequiredBackstageService<IDateTimeProvider>();
        this._machineIdProvider = serviceProvider.GetRequiredBackstageService<IMachineIdProvider>();
        this._userIdentityProvider = serviceProvider.GetRequiredBackstageService<IUserIdentityProvider>();
        this._applicationInfo = serviceProvider.GetRequiredBackstageService<IApplicationInfoProvider>().Application;
        this._leaseStore = serviceProvider.GetRequiredBackstageService<LicenseLeaseStore>();
        this._logger = serviceProvider.GetLoggerFactory().Licensing();
        this._eventDispatcher = serviceProvider.GetBackstageService<IEventDispatcher>();
        this._options = options;
    }

    /// <summary>
    /// Gets a lease for a license server: the stored one when it is still valid and not due for renewal, and a freshly
    /// downloaded one otherwise.
    /// </summary>
    /// <param name="licenseServerUrl">The URL of the license server.</param>
    /// <param name="product">The product whose licence pool the server should allocate from, or <see langword="null"/> for any.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <remarks>
    /// <para>
    /// A download is what takes a seat from the pool of the customer, and it happens only when there is no stored
    /// lease, when the stored one has expired, or when it is past its renew time. With the defaults of a server -- a
    /// three-day lease renewed after two -- one machine therefore contacts the server about once every two days,
    /// whatever the number of builds in between, and a renewal on a machine the user already holds prolongs a seat
    /// rather than allocating one.
    /// </para>
    /// <para>
    /// A renewal that fails while the stored lease is still valid publishes a
    /// <see cref="LicenseLeaseRenewalFailedEvent"/>, so that the user interface can warn the person, and keeps that
    /// lease and reports nothing to the caller:
    /// the build has a licence, and failing it because the server is briefly unreachable would be worse than the
    /// problem. PostSharp reported such a failure as an error although it went on using the lease.
    /// </para>
    /// </remarks>
    public async ValueTask<LicenseLeaseResult> GetLeaseAsync(
        string licenseServerUrl,
        LicenseProduct? product = null,
        CancellationToken cancellationToken = default )
    {
        var now = this._dateTimeProvider.UtcNow;

        if ( this._leaseStore.TryGetLease( licenseServerUrl, out var storedLease ) )
        {
            if ( storedLease.EndTime < now )
            {
                this._logger.Trace?.Log( $"The lease of '{licenseServerUrl}' ended on {storedLease.EndTime:u}. Acquiring a new one." );
                this._leaseStore.RemoveLease( licenseServerUrl );
            }
            else if ( storedLease.RenewTime < now )
            {
                this._logger.Trace?.Log( $"The lease of '{licenseServerUrl}' is due for renewal since {storedLease.RenewTime:u}." );

                var renewalResult = await this.DownloadLeaseAsync( licenseServerUrl, product, cancellationToken );

                if ( renewalResult.IsSuccess )
                {
                    return renewalResult;
                }

                this._logger.Warning?.Log(
                    $"Could not renew the lease of '{licenseServerUrl}': {renewalResult.ErrorMessage} The lease held until {storedLease.EndTime:u} is used instead." );

                // The build is licensed and hears nothing. The person is told, because the lease they are living on
                // has an end and this is the only warning they will get before it arrives.
                this._eventDispatcher?.Publish( new LicenseLeaseRenewalFailedEvent( licenseServerUrl, storedLease.EndTime, renewalResult.ErrorMessage! ) );

                return LicenseLeaseResult.Success( storedLease );
            }
            else
            {
                this._logger.Trace?.Log( $"Using the lease of '{licenseServerUrl}', which is valid until {storedLease.EndTime:u}." );

                return LicenseLeaseResult.Success( storedLease );
            }
        }

        return await this.DownloadLeaseAsync( licenseServerUrl, product, cancellationToken );
    }

    /// <summary>
    /// Contacts a license server unconditionally and stores what it leases.
    /// </summary>
    /// <param name="licenseServerUrl">The URL of the license server.</param>
    /// <param name="product">The product whose licence pool the server should allocate from, or <see langword="null"/> for any.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    public async ValueTask<LicenseLeaseResult> DownloadLeaseAsync(
        string licenseServerUrl,
        LicenseProduct? product = null,
        CancellationToken cancellationToken = default )
    {
        var requestUri = this.GetRequestUri( licenseServerUrl, product );
        this._logger.Trace?.Log( $"Leasing a license from '{requestUri}'." );

        HttpStatusCode statusCode;
        string body;

        try
        {
            using var client = this._httpClientFactory.Create(
                new HttpClientOptions
                {
                    UseDefaultCredentials = this._options.LicenseServerUsesDefaultCredentials, Timeout = this._options.LicenseServerTimeout
                } );

            // The headers are enough to begin with: the body is read by ReadBoundedAsync, which stops at the bound.
            // Waiting for the whole body here would buffer whatever the server chose to send before anything could
            // object to its size.
            using var response = await client
                .GetAsync( requestUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken )
                .ConfigureAwait( false );

            statusCode = response.StatusCode;

            // The body is read before the status is inspected, because the body of an HTTP 403 is the explanation
            // that the administrator of the server configured and is the most useful thing to show the user.
            body = await ReadBoundedAsync( response.Content, cancellationToken ).ConfigureAwait( false );
        }
        catch ( OperationCanceledException ) when ( cancellationToken.IsCancellationRequested )
        {
            throw;
        }
        catch ( OperationCanceledException )
        {
            // The two target frameworks disagree on what is nested inside the exception that HttpClient raises when
            // its own timeout elapses, so the timeout is recognized from the type alone.
            return LicenseLeaseResult.Failure(
                $"The license server at '{licenseServerUrl}' did not answer within {this._options.LicenseServerTimeout.TotalSeconds} seconds." );
        }
        catch ( Exception e )
        {
            // Every transport failure lands here: the host does not resolve, the connection is refused, the
            // certificate is rejected. The message of the exception is the only thing that tells them apart.
            return LicenseLeaseResult.Failure( $"Cannot get a lease from the license server at '{licenseServerUrl}': {e.Message}" );
        }

        if ( statusCode == HttpStatusCode.Forbidden )
        {
            var denial = body.Trim();

            return LicenseLeaseResult.Failure(
                denial.Length == 0
                    ? $"The license server at '{licenseServerUrl}' refused to grant a lease."
                    : denial );
        }

        if ( statusCode != HttpStatusCode.OK )
        {
            return LicenseLeaseResult.Failure( $"The license server at '{licenseServerUrl}' returned {(int) statusCode} {statusCode}." );
        }

        var now = this._dateTimeProvider.UtcNow;

        if ( !LicenseLease.TryDeserialize( body, now, out var lease ) )
        {
            return LicenseLeaseResult.Failure( $"The license server at '{licenseServerUrl}' returned an invalid response." );
        }

        if ( lease.EndTime < now )
        {
            // Without this guard a server whose clock is behind the client's makes every process download a lease,
            // find it expired, discard it and download again, for ever.
            return LicenseLeaseResult.Failure(
                $"The license server at '{licenseServerUrl}' returned a lease that ended on {lease.EndTime:u}, which is in the past. The clock of the server may be wrong." );
        }

        // Failing to store the lease costs one request the next time one is needed, so it is logged and no more; the
        // lease itself is good and is returned. PostSharp reported an error and returned the lease, so its caller saw
        // both a success and a failure.
        this._leaseStore.SetLease( licenseServerUrl, lease );

        return LicenseLeaseResult.Success( lease );
    }

    /// <summary>
    /// Builds the request that a license server expects.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The shape is the one PostSharp has been sending since version 5 and that deployed servers parse, so it is kept
    /// exactly: the arguments and their order, the machine name followed by a hyphen and the lower-case hexadecimal
    /// hash of the machine identifier, and the build date in the round-trip format. A server strips that hash suffix
    /// before it compares the machine name to its list of build servers, so the shape is load-bearing.
    /// </para>
    /// <para>
    /// The product is the one argument PostSharp never sent. A server reads an absent one as "any product", so adding
    /// it is compatible, and it lets one server hold the licences of several products side by side.
    /// </para>
    /// <para>
    /// A URL that already carries a query string is used verbatim, as PostSharp did. Registration refuses such a URL,
    /// so this can only be reached through a license string supplied directly to the build.
    /// </para>
    /// </remarks>
    private string GetRequestUri( string licenseServerUrl, LicenseProduct? product )
    {
        if ( licenseServerUrl.IndexOf( "?", StringComparison.Ordinal ) >= 0 )
        {
            return licenseServerUrl;
        }

        var machineHash = HashUtilities.ComputeStringHash64( this._machineIdProvider.MachineId );

        var builder = new StringBuilder( licenseServerUrl.TrimEnd( '/' ) );
        builder.Append( "/Lease.ashx?user=" ).Append( Uri.EscapeDataString( this._userIdentityProvider.UserName ) );
        builder.Append( "&machine=" ).Append( Uri.EscapeDataString( this._userIdentityProvider.MachineName ) );
        builder.Append( '-' ).Append( machineHash.ToString( "x", CultureInfo.InvariantCulture ) );

        // A server reads an absent version as 4.9.9, that is, as a client older than PostSharp 5, so a version is
        // always sent even when the application does not declare one.
        builder.Append( "&version=" ).Append( Uri.EscapeDataString( this._applicationInfo.PackageVersion ?? "0.0" ) );

        // A test application has no build date, and an empty argument is what a server fails to parse, so the
        // earliest representable instant stands for "unknown" and is a valid round-trip value.
        builder.Append( "&buildDate=" )
            .Append( Uri.EscapeDataString( (this._applicationInfo.BuildDate ?? DateTime.MinValue).ToString( "o", CultureInfo.InvariantCulture ) ) );

        if ( product != null )
        {
            builder.Append( "&product=" ).Append( Uri.EscapeDataString( product.Value.ToString() ) );
        }

        return builder.ToString();
    }

    /// <summary>
    /// Reads at most <see cref="_maxResponseLength"/> bytes of a response.
    /// </summary>
    /// <remarks>
    /// The bound is taken off the stream rather than checked afterwards. The declared length is the word of the
    /// server and may be absent or untrue, so a check against it bounds nothing; and the request asks only for the
    /// headers, so nothing has been buffered by the time this runs. A server, or a proxy standing in front of one,
    /// therefore cannot make a build allocate a body of its choosing.
    /// </remarks>
    private static async Task<string> ReadBoundedAsync( HttpContent content, CancellationToken cancellationToken )
    {
        using var stream = await content.ReadAsStreamAsync().ConfigureAwait( false );

        var buffer = new byte[_maxResponseLength];
        var count = 0;

        while ( count < buffer.Length )
        {
            var read = await stream.ReadAsync( buffer, count, buffer.Length - count, cancellationToken ).ConfigureAwait( false );

            if ( read == 0 )
            {
                break;
            }

            count += read;
        }

        return GetEncoding( content ).GetString( buffer, 0, count );
    }

    /// <summary>
    /// Gets the encoding of a response, which matters because the body of a denial is a sentence an administrator
    /// wrote and may be in any language. An absent or unknown encoding is read as UTF-8, which is what a lease is.
    /// </summary>
    private static Encoding GetEncoding( HttpContent content )
    {
        var charSet = content.Headers.ContentType?.CharSet;

        if ( string.IsNullOrEmpty( charSet ) )
        {
            return Encoding.UTF8;
        }

        try
        {
            // ReSharper disable once RedundantSuppressNullableWarningExpression
            return Encoding.GetEncoding( charSet!.Trim( '"' ) );
        }
        catch ( ArgumentException )
        {
            return Encoding.UTF8;
        }
    }
}