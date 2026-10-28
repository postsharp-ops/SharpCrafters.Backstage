// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Telemetry;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SharpCrafters.Backstage.Application;

/// <summary>
/// The <see cref="ITelemetryConfigurationService"/> of a composite product. It writes a consent to every product, so
/// that one switch in the user interface applies to all of them, and reports the maximal effective consent over all
/// products.
/// </summary>
/// <remarks>
/// The device identifier and the salts are those of the primary product, because the outer service provider reports
/// the telemetry of the primary product. A product that is not the primary product activates its own telemetry when its
/// own services report.
/// </remarks>
internal sealed class CompositeTelemetryConfigurationService : ITelemetryConfigurationService
{
    private readonly CompositeProductServices _products;

    public CompositeTelemetryConfigurationService( CompositeProductServices products )
    {
        this._products = products;
    }

    private ITelemetryConfigurationService Primary => this._products.Primary.TelemetryConfigurationService;

    private IEnumerable<ITelemetryConfigurationService> Services => this._products.Products.Select( p => p.TelemetryConfigurationService );

    /// <inheritdoc />
    /// <remarks>
    /// Only the service of the primary product is initialized here, so that the inner service providers are not created
    /// when the outer one is initialized. The service of another product is initialized when it is created.
    /// </remarks>
    public void Initialize() => this.Primary.Initialize();

    public void EnsureActivated() => this.Primary.EnsureActivated();

    public bool IsActivated => this.Primary.IsActivated;

    [Obsolete( "Use SetConsent instead." )]
    public void SetStatus( bool enabled ) => CompositeBackstageProduct.ForEach( this.Services, s => s.SetStatus( enabled ) );

    [Obsolete( "Use SetConsent instead." )]
    public void SetStatus( TelemetryScenario scenario, bool enabled ) => CompositeBackstageProduct.ForEach( this.Services, s => s.SetStatus( scenario, enabled ) );

    public void SetConsent( TelemetryConsent action ) => CompositeBackstageProduct.ForEach( this.Services, s => s.SetConsent( action ) );

    public void SetConsent( TelemetryScenario scenario, TelemetryConsent action )
        => CompositeBackstageProduct.ForEach( this.Services, s => s.SetConsent( scenario, action ) );

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// The comparison is made for each product against its own consent, so a product whose consent differs keeps it.
    /// This matters because the services call this method without a gesture of the user: a usage session changes
    /// <see cref="TelemetryConsent.Default"/> to <see cref="TelemetryConsent.Yes"/>, and it must not change the
    /// <see cref="TelemetryConsent.No"/> of another product.
    /// </para>
    /// <para>
    /// The result is that of the primary product, whose pipeline reacts to it.
    /// </para>
    /// </remarks>
    public bool CompareExchangeConsent( TelemetryScenario scenario, TelemetryConsent newAction, TelemetryConsent oldAction )
    {
        var result = this.Primary.CompareExchangeConsent( scenario, newAction, oldAction );

        CompositeBackstageProduct.ForEach( this.Services.Skip( 1 ), s => s.CompareExchangeConsent( scenario, newAction, oldAction ) );

        return result;
    }

    public Guid DeviceId => this.Primary.DeviceId;

    /// <inheritdoc />
    /// <remarks>
    /// The result is the maximal effective consent over all products, in the order <see cref="TelemetryConsent.No"/>,
    /// <see cref="TelemetryConsent.Default"/>, <see cref="TelemetryConsent.Yes"/>, unless telemetry is disabled for the
    /// primary product at the process level. Reading it creates the inner service providers.
    /// </remarks>
    public TelemetryConsent GetEffectiveConsent( TelemetryScenario scenario ) => this.GetEffectiveConsentAndReason( scenario ).Consent;

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// When telemetry is disabled for the primary product at the process level (see <see cref="IsGloballyEnabled"/>),
    /// for instance by its opt-out environment variable, the result is that of the primary product. The outer provider
    /// runs the pipeline of the primary product, so the consent of another product must not override such an opt-out.
    /// </para>
    /// <para>
    /// Otherwise, the result is that of the product with the maximal effective consent (see
    /// <see cref="GetEffectiveConsent"/>). When several products have it, the first of them, starting with the primary
    /// product, gives the reason.
    /// </para>
    /// </remarks>
    public (TelemetryConsent Consent, TelemetryDisabledReason Reason) GetEffectiveConsentAndReason( TelemetryScenario scenario )
    {
        var result = this.Primary.GetEffectiveConsentAndReason( scenario );

        if ( !this.Primary.IsGloballyEnabled )
        {
            return result;
        }

        foreach ( var service in this.Services.Skip( 1 ) )
        {
            var candidate = service.GetEffectiveConsentAndReason( scenario );

            if ( GetRank( candidate.Consent ) > GetRank( result.Consent ) )
            {
                result = candidate;
            }
        }

        return result;
    }

    /// <summary>
    /// Gets the rank of a consent, from the most restrictive to the most permissive. The declaration order of
    /// <see cref="TelemetryConsent"/> is not this order: <see cref="TelemetryConsent.Default"/> is declared first, and it
    /// is between the two others.
    /// </summary>
    private static int GetRank( TelemetryConsent consent )
        => consent switch
        {
            TelemetryConsent.No => 0,
            TelemetryConsent.Default => 1,
            TelemetryConsent.Yes => 2,
            _ => throw new ArgumentOutOfRangeException( nameof(consent), consent, null )
        };

    public bool IsGloballyEnabled => this.Primary.IsGloballyEnabled;

    public void ResetDeviceId() => CompositeBackstageProduct.ForEach( this.Services, s => s.ResetDeviceId() );

    public void ResetReportedIssues() => CompositeBackstageProduct.ForEach( this.Services, s => s.ResetReportedIssues() );

    public long GetSalt( TelemetrySaltKind kind ) => this.Primary.GetSalt( kind );

    [Obsolete( "Use GetSalt(TelemetrySaltKind.Matomo) instead." )]
    public long MatomoSalt => this.Primary.MatomoSalt;

    [Obsolete( "Use GetSalt(TelemetrySaltKind.UsageTracking) instead." )]
    public long UsageTrackingSalt => this.Primary.UsageTrackingSalt;

    [Obsolete( "Use GetSalt(TelemetrySaltKind.ExceptionReport) instead." )]
    public long ExceptionReportingSalt => this.Primary.ExceptionReportingSalt;

    [Obsolete( "Use GetSalt(TelemetrySaltKind.LicenseAudit) instead." )]
    public long LicenseAuditSalt => this.Primary.LicenseAuditSalt;
}
