// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using Metalama.Backstage.Diagnostics;
using Metalama.Backstage.Extensibility;
using Metalama.Backstage.Infrastructure;
using Metalama.Backstage.Licensing.Audit;
using Metalama.Backstage.Licensing.Consumption.Sources;
using Metalama.Backstage.Licensing.Licenses;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Metalama.Backstage.Licensing.Consumption;

internal sealed class LicenseConsumer : ILicenseConsumer
{
    private readonly ILogger _logger;
    private readonly LicenseConsumptionData? _license;
    private readonly BackstageBackgroundTasksService _backgroundTasksService;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly ILicenseAuditManager? _licenseAuditManager;

    private DateTime _lastAuditTime = DateTime.MinValue;

    private LicenseConsumer( IServiceProvider services, LicenseConsumptionData? license )
    {
        this._license = license;
        this._logger = services.GetLoggerFactory().Licensing();
        this._dateTimeProvider = services.GetRequiredBackstageService<IDateTimeProvider>();
        this._licenseAuditManager = services.GetBackstageService<ILicenseAuditManager>();
        this._backgroundTasksService = services.GetRequiredBackstageService<BackstageBackgroundTasksService>();
    }

    public static ILicenseConsumer Create(
        IServiceProvider services,
        IReadOnlyList<ILicenseSource> licenseSources,
        out ImmutableArray<LicensingMessage> messages )
    {
        var messagesBuilder = ImmutableArray.CreateBuilder<LicensingMessage>();

        var logger = services.GetLoggerFactory().Licensing();

        LicenseConsumptionData? licenseConsumptionData = null;

        foreach ( var source in licenseSources.OrderBy( s => s.Priority ) )
        {
            var license = source.GetLicense( ReportMessage );

            if ( license == null )
            {
                logger.Trace?.Log( $"'{source.GetType().Name}' license source provided no license." );

                continue;
            }

            if ( !license.TryGetLicenseConsumptionData( out licenseConsumptionData, out var errorMessage ) )
            {
                _ = license.TryGetProperties( out var registrationData, out _ );
                var message = registrationData == null ? "A license" : $"The {registrationData.Description}";
                message += $" {errorMessage}.";

                if ( registrationData is { IsSelfCreated: false } )
                {
                    message += $" License key ID: '{registrationData.LicenseId}'.";
                }

                if ( source.GetType() != typeof(UserProfileLicenseSource) )
                {
                    message += $" The license key originates from {source.Description}.";
                }

                ReportMessage( new LicensingMessage( message ) );

                continue;
            }

            if ( !string.IsNullOrEmpty( licenseConsumptionData.LicensedNamespace ) )
            {
                logger.Warning?.Log( $"The license '{licenseConsumptionData.LicenseString}' has a namespace constraint, which is no longer supported." );

                continue;
            }

            break;
        }

        messages = messagesBuilder.ToImmutable();

        return new LicenseConsumer( services, licenseConsumptionData );

        void ReportMessage( LicensingMessage message )
        {
            messagesBuilder.Add( message );
            logger.Warning?.Log( message.Text );
        }
    }

    /// <inheritdoc />
    public bool CanConsume( Predicate<LicenseConsumptionData> predicate )
    {
        if ( this._license == null )
        {
            this._logger.Warning?.Log( "No license provided." );

            return false;
        }

        if ( predicate( this._license ) )
        {
            this.AuditIfNecessary();

            return true;
        }
        else
        {
            return false;
        }
    }

    private void AuditIfNecessary()
    {
        // Audit the use of the license once per day (more time checks are performed by the license audit manager).
        if ( this._license != null && this._lastAuditTime.AddDays( 1 ) < this._dateTimeProvider.UtcNow )
        {
            this._lastAuditTime = this._dateTimeProvider.UtcNow;

            if ( this._licenseAuditManager != null )
            {
                this._backgroundTasksService.Enqueue( () => this._licenseAuditManager.ReportLicense( this._license ) );
            }
            else
            {
                this._logger.Warning?.Log( $"License audit is skipped because there is no {nameof(ILicenseAuditManager)}." );
            }
        }
    }

    public bool IsTrialLicense => this._license?.LicenseType == LicenseType.Evaluation;

    public string? LicenseString => this._license?.LicenseString;
}