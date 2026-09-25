// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Extensibility;
using SharpCrafters.Backstage.Licensing.Consumption;
using SharpCrafters.Backstage.Licensing.LicenseServer;
using SharpCrafters.Backstage.Licensing.Registration;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace SharpCrafters.Backstage.Licensing.Licenses;

/// <summary>
/// A licence leased from a license server for a limited period, as opposed to a <see cref="License"/>, which is a
/// licence key registered once and valid until it expires.
/// </summary>
/// <remarks>
/// <para>
/// Everything that a licence key means — its signature, its revocation, its validity dates, its subscription, its
/// product — is decided by an inner <see cref="License"/> built from the leased key, so none of those rules is stated
/// twice. What this class adds is the lease itself and the rule that a leased key must be eligible for a license
/// server.
/// </para>
/// <para>
/// The lease is resolved at most once per instance. The consumption service asks a licence that failed for its
/// registration properties in order to name it in the message, so without the memo a failed acquisition would contact
/// the server twice for one consumer.
/// </para>
/// <para>
/// The resolution happens while the consumer is built, like that of every other licence, and therefore takes a seat
/// whether or not a requirement ends up using this licence. Deferring it until a requirement asked was tried and
/// removed: the requirements are not known while the consumer is being built, so a higher-priority license key that is
/// valid but not eligible for a particular requirement would leave a build unlicensed that this lease would have
/// licensed. The cost is a seat held by a machine that did not need it; the alternative is a build that fails although
/// a licence was available. See <c>docs/license-server.md</c>.
/// </para>
/// </remarks>
internal sealed class LeasedLicense : AuditableLicense
{
    private readonly IServiceProvider _services;
    private readonly LicenseServerClient _client;
    private readonly ILicenseProductCatalog _catalog;

    /// <summary>
    /// What resolving the lease produced, computed on the first call and reused afterwards.
    /// </summary>
    private LicenseLeaseResult? _resolution;

    private License? _innerLicense;

    internal LeasedLicense( string licenseServerUrl, IServiceProvider services ) : base( services )
    {
        this.LicenseServerUrl = licenseServerUrl;
        this._services = services;
        this._client = services.GetRequiredBackstageService<LicenseServerClient>();
        this._catalog = services.GetRequiredBackstageService<ILicenseProductCatalog>();
    }

    /// <summary>
    /// Gets the URL of the license server that leases this licence.
    /// </summary>
    public string LicenseServerUrl { get; }

    /// <inheritdoc />
    public override async ValueTask<LicenseConsumptionResult> GetConsumptionPropertiesAsync(
        LicenseConsumptionOptions options,
        CancellationToken cancellationToken = default )
    {
        var leaseResult = await this.ResolveAsync( false, cancellationToken );

        if ( !leaseResult.IsSuccess )
        {
            return LicenseConsumptionResult.Failure( leaseResult.ErrorMessage!, LicensingMessageKind.LicenseServerLeaseFailed );
        }

        var innerResult = await this.GetInnerLicense( leaseResult.Lease! ).GetConsumptionPropertiesAsync( options, cancellationToken );

        if ( !innerResult.IsSuccess )
        {
            return innerResult;
        }

        if ( !this.IsLicenseServerEligible( leaseResult.Lease! ) )
        {
            // The equivalent of PostSharp's PS0149. It is reported as a message and not raised, so that another
            // licence can still satisfy the requirement.
            return LicenseConsumptionResult.Failure(
                $"the license key leased from '{this.LicenseServerUrl}' is not eligible for a license server",
                LicensingMessageKind.LicenseServerLeaseFailed );
        }

        return innerResult;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Registration contacts the server rather than reading the stored lease, so that registering a URL tells the user
    /// at once whether the server answers and has a licence for them.
    /// </remarks>
    public override ValueTask<LicenseRegistrationPropertiesResult> GetRegistrationPropertiesAsync( CancellationToken cancellationToken = default )
        => this.GetRegistrationPropertiesAsync( true, cancellationToken );

    /// <summary>
    /// Reads the properties of the leased licence, acquiring the lease if necessary.
    /// </summary>
    /// <param name="forceRenewal">
    /// <see langword="true"/> to contact the server even when the stored lease is valid and not yet due for renewal.
    /// Registering a server does that, so that the user learns at once whether it answers; so does
    /// <see cref="ILicenseRegistrationService.AcquireLeaseAsync"/> when it is asked to.
    /// </param>
    /// <param name="cancellationToken">A cancellation token.</param>
    internal async ValueTask<LicenseRegistrationPropertiesResult> GetRegistrationPropertiesAsync(
        bool forceRenewal,
        CancellationToken cancellationToken = default )
    {
        var leaseResult = await this.ResolveAsync( forceRenewal, cancellationToken );

        if ( !leaseResult.IsSuccess )
        {
            return LicenseRegistrationPropertiesResult.Failure( leaseResult.ErrorMessage! );
        }

        var innerResult = await this.GetInnerLicense( leaseResult.Lease! ).GetRegistrationPropertiesAsync( cancellationToken );

        if ( !innerResult.IsSuccess )
        {
            return innerResult;
        }

        return LicenseRegistrationPropertiesResult.Success(
            ToLicenseServerProperties( innerResult.Properties!, this.LicenseServerUrl, leaseResult.Lease!, this._catalog ) );
    }

    /// <inheritdoc />
    public override async ValueTask<LicenseRegistrationBlocker> GetRegistrationBlockerAsync( CancellationToken cancellationToken = default )
    {
        var consumptionResult = await this.GetConsumptionPropertiesAsync( LicenseConsumptionOptions.ForRegistration, cancellationToken );

        if ( !consumptionResult.IsSuccess )
        {
            return LicenseRegistrationBlocker.Unusable( consumptionResult.ErrorMessage! );
        }

#pragma warning disable CS0612 // Type or member is obsolete
        if ( consumptionResult.Properties!.IsRedistributable )
#pragma warning restore CS0612
        {
            return LicenseRegistrationBlocker.Redistribution;
        }

        return LicenseRegistrationBlocker.None;
    }

    /// <summary>
    /// Turns the registration properties of the leased licence key into those of the license server, so that what is
    /// registered and listed is the server and not the key that it happens to lease today.
    /// </summary>
    internal static LicenseRegistrationProperties ToLicenseServerProperties(
        LicenseRegistrationProperties leasedKeyProperties,
        string licenseServerUrl,
        LicenseLease lease,
        ILicenseProductCatalog catalog )
        => leasedKeyProperties with
        {
            LicenseString = licenseServerUrl,
            LicenseServerUrl = licenseServerUrl,
            Lease = new LicenseLeaseProperties( lease.StartTime, lease.EndTime, lease.RenewTime ),

            // A registered URL is stored in the group of the first version of this family that understands a license
            // server, whatever the product of the licence leased today, which the server may change tomorrow. The
            // family is asked rather than the key, because a URL has no content to judge, and the families differ:
            // PostSharp has had license servers since before it recorded which version was asking.
            MinVersion = catalog.MinimalLicenseServerVersion
        };

    private async ValueTask<LicenseLeaseResult> ResolveAsync( bool forceDownload, CancellationToken cancellationToken )
    {
        if ( this._resolution is { } resolution )
        {
            return resolution;
        }

        var product = this._catalog.LicenseServerProduct;

        var result = forceDownload
            ? await this._client.DownloadLeaseAsync( this.LicenseServerUrl, product, cancellationToken )
            : await this._client.GetLeaseAsync( this.LicenseServerUrl, product, cancellationToken );

        this._resolution = result;

        return result;
    }

    private License GetInnerLicense( LicenseLease lease ) => this._innerLicense ??= new License( lease.LicenseKey, this._services );

    /// <summary>
    /// Determines whether the leased licence key may be leased at all.
    /// </summary>
    /// <remarks>
    /// The rule is that of the licence key itself, which
    /// <see cref="LicenseKeyDataExtensions.ToLicenseRegistrationProperties"/> computes: the explicit field of the key
    /// if it has one, otherwise a per-usage key is refused, otherwise an identifier in the range issued before 5.0.
    /// </remarks>
    private bool IsLicenseServerEligible( LicenseLease lease )
        => LicenseKeyData.TryDeserialize( lease.LicenseKey, out var keyData, out _ )
           && keyData.ToLicenseRegistrationProperties( this._catalog, lease.LicenseKey ).LicenseServerEligible;

    /// <inheritdoc />
    public override bool Equals( object? obj )
        => obj is LeasedLicense other && string.Equals( this.LicenseServerUrl, other.LicenseServerUrl, StringComparison.OrdinalIgnoreCase );

    /// <inheritdoc />
    public override int GetHashCode() => StringComparer.OrdinalIgnoreCase.GetHashCode( this.LicenseServerUrl );

    /// <inheritdoc />
    public override string ToString() => this.LicenseServerUrl;
}
