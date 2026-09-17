// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Diagnostics;
using SharpCrafters.Backstage.Extensibility;
using SharpCrafters.Backstage.Infrastructure;
using SharpCrafters.Backstage.Licensing.Audit;
using SharpCrafters.Backstage.Licensing.Consumption;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace SharpCrafters.Backstage.Licensing.Licenses;

/// <summary>
/// The base class of every <see cref="ILicense"/> of this assembly, which adds the reporting of a consumed licence to
/// the licence audit.
/// </summary>
/// <remarks>
/// The audit is what records that a licence was really used, and it is how a licence key is accounted for. A licence
/// leased from a license server is accounted for twice: here, like any other, and in the ledger of the server, which
/// is what the customer reads to see who holds a seat.
/// </remarks>
internal abstract class AuditableLicense : ILicense
{
    private readonly ILicenseAuditManager? _licenseAuditManager;
    private readonly BackstageBackgroundTasksService _backgroundTasksService;

    protected ILogger Logger { get; }

    internal AuditableLicense( IServiceProvider services )
    {
        this._licenseAuditManager = services.GetBackstageService<ILicenseAuditManager>();
        this._backgroundTasksService = services.GetRequiredBackstageService<BackstageBackgroundTasksService>();
        this.Logger = services.GetLoggerFactory().Licensing();
    }

    /// <inheritdoc />
    public abstract ValueTask<LicenseRegistrationBlocker> GetRegistrationBlockerAsync( CancellationToken cancellationToken = default );

    /// <inheritdoc />
    public abstract ValueTask<LicenseConsumptionResult> GetConsumptionPropertiesAsync(
        LicenseConsumptionOptions options,
        CancellationToken cancellationToken = default );

    /// <inheritdoc />
    public abstract ValueTask<LicenseRegistrationPropertiesResult> GetRegistrationPropertiesAsync( CancellationToken cancellationToken = default );

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// The method stays synchronous although reading the properties of the licence is not, because the whole
    /// operation is enqueued on the background tasks service, which is where the audit already ran. It is called from
    /// the consumption of a licence, which is on the critical path of a compilation and must not await anything.
    /// </para>
    /// </remarks>
    public void ReportUse()
    {
        if ( this._licenseAuditManager == null )
        {
            this.Logger.Warning?.Log( $"License audit is skipped because there is no {nameof(ILicenseAuditManager)}." );

            return;
        }

        this._backgroundTasksService.Enqueue( this.ReportUseAsync );
    }

    private async Task ReportUseAsync()
    {
        // The licence has already been examined by the time its use is reported, so this completes synchronously
        // even for a licence leased from a server, whose lease is resolved once per instance.
        var result = await this.GetConsumptionPropertiesAsync( LicenseConsumptionOptions.Default );

        if ( !result.IsSuccess )
        {
            this.Logger.Warning?.Log( $"License audit is skipped: {result.ErrorMessage}" );

            return;
        }

        if ( !result.Properties.IsAuditable )
        {
            this.Logger.Warning?.Log( "License audit is skipped: the license is not auditable." );

            return;
        }

        this._licenseAuditManager!.ReportLicense( result.Properties );
    }
}
