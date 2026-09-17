// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Application;
using SharpCrafters.Backstage.Diagnostics;
using SharpCrafters.Backstage.Extensibility;
using SharpCrafters.Backstage.Licensing.Licenses;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SharpCrafters.Backstage.Licensing.Consumption.Sources;

internal sealed class UnattendedLicenseSource : ILicenseSource, ILicense
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger _logger;
    private readonly IApplicationInfo _applicationInfo;

    public string Description => "unattended license source";

    public LicenseSourceKind Kind => LicenseSourceKind.Unattended;

    public UnattendedLicenseSource( IServiceProvider serviceProvider )
    {
        this._serviceProvider = serviceProvider;
        this._applicationInfo = serviceProvider.GetRequiredBackstageService<IApplicationInfoProvider>().CurrentApplication;
        this._logger = serviceProvider.GetLoggerFactory().Licensing();
    }

    /// <inheritdoc />
    public IEnumerable<ILicense> GetLicenses( Action<LicensingMessage> reportMessage )
    {
        if ( this._applicationInfo.IsUnattendedProcess( this._serviceProvider.GetLoggerFactory() ) )
        {
            this._logger.Trace?.Log( "Providing an unattended process license." );

            yield return this;
        }
        else
        {
            this._logger.Trace?.Log( "The process is attended. Not providing an unattended process license." );
        }
    }

    public ValueTask<LicenseRegistrationBlocker> GetRegistrationBlockerAsync( CancellationToken cancellationToken = default )
        => throw new NotSupportedException( "Unattended license source doesn't support license registration." );

    ValueTask<LicenseConsumptionResult> ILicense.GetConsumptionPropertiesAsync(
        LicenseConsumptionOptions options,
        CancellationToken cancellationToken )
        => new(
            LicenseConsumptionResult.Success(
                new LicenseConsumptionProperties(
                    LicenseProduct.MetalamaProfessional,
                    LicenseType.Unattended,
                    null,
                    "Unattended Process License",
                    new Version( 0, 0 ),
                    null,
                    false,
                    false,
                    null,
                    null,
                    SubscriptionStatus.None,
                    LicenseGeneration.Current,
                    ServicingPhase.LongTerm ) ) );

    ValueTask<LicenseRegistrationPropertiesResult> ILicense.GetRegistrationPropertiesAsync( CancellationToken cancellationToken )
        => throw new NotSupportedException( "Unattended license source doesn't support license registration." );

    public void ReportUse() { }

    event Action? ILicenseSource.Changed { add { } remove { } }

    public LicenseSourcePriority Priority => LicenseSourcePriority.Unattended;

    public bool SupportsRegistration => false;
}
