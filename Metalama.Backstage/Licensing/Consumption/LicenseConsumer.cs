// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using Metalama.Backstage.Diagnostics;
using Metalama.Backstage.Extensibility;
using Metalama.Backstage.Infrastructure;
using Metalama.Backstage.Licensing.Consumption.Sources;
using Metalama.Backstage.Licensing.Licenses;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Metalama.Backstage.Licensing.Consumption;

internal sealed class LicenseConsumer : ILicenseConsumer
{
    public ImmutableArray<LicensingMessage> Messages { get; }

    private readonly ImmutableArray<(ILicense License, LicenseConsumptionProperties Properties)> _licenses;
    private readonly IDateTimeProvider _dateTimeProvider;

    private DateTime _lastAuditTime = DateTime.MinValue;

    private LicenseConsumer(
        IServiceProvider services,
        ImmutableArray<(ILicense License, LicenseConsumptionProperties Properties)> licenses,
        ImmutableArray<LicensingMessage> messages )
    {
        this.Messages = messages;
        this._licenses = licenses;
        this._dateTimeProvider = services.GetRequiredBackstageService<IDateTimeProvider>();
    }

    public static ILicenseConsumer Create(
        LicenseConsumptionOptions options,
        IServiceProvider services,
        IEnumerable<ILicenseSource> licenseSources )
    {
        var messagesBuilder = ImmutableArray.CreateBuilder<LicensingMessage>();

        var logger = services.GetLoggerFactory().Licensing();

        var licenses = licenseSources.OrderBy( s => s.Priority ).SelectMany( s => s.GetLicenses( ReportMessage ).Select( l => (License: l, Source: s) ) );

        var validLicenses = ImmutableArray.CreateBuilder<(ILicense License, LicenseConsumptionProperties Properties)>();

        foreach ( var license in licenses )
        {
            if ( !license.License.TryGetConsumptionProperties( options, out var licenseConsumptionData, out var errorMessage ) )
            {
                _ = license.License.TryGetRegistrationProperties( out var registrationData, out _ );
                var message = registrationData == null ? "A license" : $"The {registrationData.Description}";
                message += $" {errorMessage}.";

                if ( registrationData is { IsSelfCreated: false } )
                {
                    message += $" License key ID: '{registrationData.LicenseId}'.";
                }

                if ( license.Source.GetType() != typeof(UserProfileLicenseSource) )
                {
                    message += $" The license key originates from {license.Source.Description}.";
                }

                ReportMessage( new LicensingMessage( message ) );

                continue;
            }

#pragma warning disable CS0612 // Type or member is obsolete
            if ( !string.IsNullOrEmpty( licenseConsumptionData.LicensedNamespace ) )
            {
                logger.Warning?.Log( $"The license '{licenseConsumptionData.LicenseString}' has a namespace constraint, which is no longer supported." );

                continue;
            }
#pragma warning restore CS0612 // Type or member is obsolete

            validLicenses.Add( (license.License, licenseConsumptionData) );
        }

        return new LicenseConsumer( services, validLicenses.ToImmutableArray(), messagesBuilder.ToImmutable() );

        void ReportMessage( LicensingMessage message )
        {
            messagesBuilder.Add( message );
            logger.Warning?.Log( message.Text );
        }
    }

    /// <inheritdoc />
    public bool TryConsume( Predicate<LicenseConsumptionProperties> predicate )
    {
        var mustAudit = false;

        if ( this._lastAuditTime.AddDays( 1 ) < this._dateTimeProvider.UtcNow )
        {
            this._lastAuditTime = this._dateTimeProvider.UtcNow;
            mustAudit = true;
        }

        foreach ( var license in this._licenses )
        {
            if ( predicate( license.Properties ) )
            {
                if ( mustAudit )
                {
                    license.License.OnConsumed();
                }

                return true;
            }
        }

        return false;
    }
}