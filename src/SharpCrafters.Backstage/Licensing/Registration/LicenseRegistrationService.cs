// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Application;
using SharpCrafters.Backstage.Configuration;
using SharpCrafters.Backstage.Diagnostics;
using SharpCrafters.Backstage.Extensibility;
using SharpCrafters.Backstage.Infrastructure;
using SharpCrafters.Backstage.Licensing.LicenseServer;
using SharpCrafters.Backstage.Licensing.Licenses;
using SharpCrafters.Backstage.UserInterface;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace SharpCrafters.Backstage.Licensing.Registration;

internal sealed class LicenseRegistrationService : ILicenseRegistrationService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger _logger;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly IUserDeviceDetectionService _userDeviceDetectionService;
    private readonly IConfigurationManager _configurationManager;
    private readonly ProductProfile _productProfile;
    private readonly ILicenseProductCatalog _catalog;
    private readonly LicenseLeaseStore _leaseStore;

    /// <summary>
    /// The version of the running product. It decides the groups of license keys that the current service reads.
    /// </summary>
    private readonly Version _currentVersion;

    public LicenseRegistrationService( IServiceProvider serviceProvider )
    {
        this._serviceProvider = serviceProvider;
        this._currentVersion = serviceProvider.GetRequiredBackstageService<IApplicationInfoProvider>().CurrentApplication.GetLicensingVersion();
        this._logger = serviceProvider.GetLoggerFactory().GetLogger( nameof(LicenseRegistrationService) );
        this._dateTimeProvider = serviceProvider.GetRequiredBackstageService<IDateTimeProvider>();
        this._userDeviceDetectionService = serviceProvider.GetRequiredBackstageService<IUserDeviceDetectionService>();
        this._configurationManager = serviceProvider.GetRequiredBackstageService<IConfigurationManager>();
        this._productProfile = serviceProvider.GetRequiredBackstageService<ProductProfile>();
        this._catalog = serviceProvider.GetRequiredBackstageService<ILicenseProductCatalog>();
        this._leaseStore = serviceProvider.GetRequiredBackstageService<LicenseLeaseStore>();

        // We intentionally omit to unsubscribe from the event because this service has generally the same lifetime as the application
        // and is never disposed of.
        serviceProvider.GetRequiredBackstageService<IConfigurationManager>().ConfigurationFileChanged += this.OnConfigurationChanged;
    }

    private void OnConfigurationChanged( ConfigurationFile obj )
    {
        if ( obj is LicensingConfiguration )
        {
            this.OnPropertyChanged( nameof(this.RegisteredLicenses) );
            this.OnPropertyChanged( nameof(this.UnsupportedRegisteredLicenseVersions) );
            this.OnPropertyChanged( nameof(this.CanRegisterTrialEdition) );
        }
    }

    private bool RequireAttendedSession( [NotNullWhen( false )] out string? errorMessage )
    {
        if ( !this._userDeviceDetectionService.IsInteractiveDevice )
        {
            errorMessage = "This command must be executed from an interactive session.";

            return false;
        }
        else
        {
            errorMessage = null;

            return true;
        }
    }

    /// <summary>
    /// Attempts to register an unsigned Metalama Community license.
    /// </summary>
    /// <returns>
    /// A value indicating whether the license has been registered.
    /// Success is indicated when a new Metalama Community license is registered
    /// as well as when an existing Metalama Community license is registered already.
    /// </returns>
    public LicenseRegistrationResult RegisterCommunityEdition( CommunityLicenseReason reason )
    {
        if ( !this.RequireAttendedSession( out var errorMessage ) )
        {
            return LicenseRegistrationResult.Failure( errorMessage );
        }

        if ( reason == CommunityLicenseReason.None )
        {
            throw new ArgumentOutOfRangeException( nameof(reason), reason, "The community license reason is invalid." );
        }

        this._logger.Trace?.Log( $"Registering {this._productProfile.Name} Community." );

        var factory = new UnsignedLicenseFactory( this._serviceProvider );
        var communityLicense = factory.CreateCommunityLicense();

        if ( !this._configurationManager.Update<LicensingConfiguration>(
                config => config.SetLicense( communityLicense, this._catalog ) with { CommunityLicenseReason = reason } ) )
        {
            return LicenseRegistrationResult.Failure( $"{this._productProfile.Name} Community is already registered." );
        }

        return LicenseRegistrationResult.Success( communityLicense );
    }

    [Obsolete]
    public LicenseRegistrationResult RegisterLegacyFreeEdition()
    {
        if ( !this.RequireAttendedSession( out var errorMessage ) )
        {
            return LicenseRegistrationResult.Failure( errorMessage );
        }

        this._logger.Trace?.Log( $"Registering {this._productProfile.Name} Free." );

        var factory = new UnsignedLicenseFactory( this._serviceProvider );
        var communityLicense = factory.CreateLegacyFreeLicense();

        this._configurationManager.Update<LicensingConfiguration>( config => config.SetLicense( communityLicense, this._catalog ) );

        return LicenseRegistrationResult.Success( communityLicense );
    }

    public LicenseRegistrationResult RegisterTrialEdition()
    {
        if ( !this.RequireAttendedSession( out var errorMessage ) )
        {
            return LicenseRegistrationResult.Failure( errorMessage );
        }

        this._logger.Trace?.Log( "Attempting to register an evaluation license." );

        if ( !this.CanRegisterTrialEditionCore( out errorMessage ) )
        {
            return LicenseRegistrationResult.Failure( errorMessage );
        }

        var factory = new UnsignedLicenseFactory( this._serviceProvider );
        var evaluationLicense = factory.CreateEvaluationLicense();

        this._configurationManager.Update<LicensingConfiguration>(
            config => config.SetLicense( evaluationLicense, this._catalog ) with { LastEvaluationStartDate = this._dateTimeProvider.UtcNow } );

        return LicenseRegistrationResult.Success( evaluationLicense );
    }

    private bool CanRegisterTrialEditionCore( [NotNullWhen( false )] out string? errorMessage )
    {
        var currentConfiguration = this._configurationManager.Get<LicensingConfiguration>();

        if ( currentConfiguration.GetRegisteredLicenses( this._currentVersion )
            .Any( l => l is { LicenseType: LicenseType.Evaluation } && l.ValidTo >= this._dateTimeProvider.UtcNow ) )
        {
            errorMessage = "The evaluation license is already active.";

            return false;
        }

        var lastEvaluationStartDate = currentConfiguration.LastEvaluationStartDate ?? DateTime.MinValue;
        var nextEvaluationStartDate = lastEvaluationStartDate + LicensingConstants.NoEvaluationPeriod + LicensingConstants.EvaluationPeriod;

        if ( nextEvaluationStartDate > this._dateTimeProvider.UtcNow )
        {
            errorMessage = $"You cannot start a new trial period until {nextEvaluationStartDate}.";
            this._logger.Warning?.Log( errorMessage );

            return false;
        }

        errorMessage = null;

        return true;
    }

    public ValueTask<LicenseRegistrationResult> RegisterLicenseAsync( string licenseString, CancellationToken cancellationToken = default )
        => this.RegisterLicenseCoreAsync( licenseString, false, cancellationToken );

    [Obsolete( "Use RegisterLicenseAsync." )]
    public LicenseRegistrationResult RegisterLicense( string licenseString ) => Block( () => this.RegisterLicenseAsync( licenseString ) );

    public ValueTask<LicenseRegistrationResult> ValidateLicenseKeyAsync( string licenseKey, CancellationToken cancellationToken = default )
        => this.RegisterLicenseCoreAsync( licenseKey, true, cancellationToken );

    /// <inheritdoc />
    public ValueTask<LicenseRegistrationResult> TestLicenseServerAsync( string licenseServerUrl, CancellationToken cancellationToken = default )
    {
        if ( !LicenseServerUrl.IsLicenseServerUrl( licenseServerUrl, out var errorMessage ) )
        {
            return new ValueTask<LicenseRegistrationResult>( LicenseRegistrationResult.Failure( errorMessage ) );
        }

        // The same path as a registration, without the write: the server is contacted and the licence it leases is
        // validated, so that what the user sees is what a registration would store.
        return this.RegisterLicenseCoreAsync( licenseServerUrl, true, cancellationToken );
    }

    [Obsolete( "Use ValidateLicenseKeyAsync." )]
    public LicenseRegistrationResult ValidateLicenseKey( string licenseKey ) => Block( () => this.ValidateLicenseKeyAsync( licenseKey ) );

    /// <inheritdoc />
    public async ValueTask<LicenseRegistrationResult> ResolveLicenseAsync( string licenseString, CancellationToken cancellationToken = default )
    {
        if ( !this.RequireAttendedSession( out var errorMessage ) )
        {
            return LicenseRegistrationResult.Failure( errorMessage );
        }

        var factory = new LicenseFactory( this._serviceProvider );

        if ( !factory.TryCreate( licenseString, out var license, out var factoryErrorMessage ) )
        {
            return LicenseRegistrationResult.Failure( factoryErrorMessage );
        }

        var registrationResult = await license.GetRegistrationPropertiesAsync( cancellationToken );

        return registrationResult.IsSuccess
            ? LicenseRegistrationResult.Success( registrationResult.Properties )
            : LicenseRegistrationResult.Failure( registrationResult.ErrorMessage );
    }

    [Obsolete( "Use ResolveLicenseAsync." )]
    public LicenseRegistrationResult ParseLicenseKey( string licenseKey ) => Block( () => this.ResolveLicenseAsync( licenseKey ) );

    /// <summary>
    /// Runs an asynchronous registration operation to completion on the calling thread, for the obsolete synchronous
    /// members that exist so that the callers of earlier versions keep compiling.
    /// </summary>
    /// <remarks>
    /// The work is started on a thread-pool thread, where <see cref="System.Threading.SynchronizationContext.Current"/>
    /// is null, so no continuation can be posted back to the thread that is blocked here; that is what deadlocks a user
    /// interface thread on .NET Framework. <c>GetResult</c> rethrows the original exception, whereas <c>Wait</c> would
    /// wrap it in an <see cref="AggregateException"/> whose message does not name the failure.
    /// </remarks>
    private static LicenseRegistrationResult Block( Func<ValueTask<LicenseRegistrationResult>> operation )
        => Task.Run( () => operation().AsTask() ).GetAwaiter().GetResult();

    private async ValueTask<LicenseRegistrationResult> RegisterLicenseCoreAsync( string licenseString, bool dry, CancellationToken cancellationToken )
    {
        if ( !this.RequireAttendedSession( out var errorMessage ) )
        {
            return LicenseRegistrationResult.Failure( errorMessage );
        }

        var factory = new LicenseFactory( this._serviceProvider );

        if ( !factory.TryCreate( licenseString, out var license, out var factoryErrorMessage ) )
        {
            return LicenseRegistrationResult.Failure( factoryErrorMessage );
        }

        var registrationResult = await license.GetRegistrationPropertiesAsync( cancellationToken );

        if ( !registrationResult.IsSuccess )
        {
            return LicenseRegistrationResult.Failure( registrationResult.ErrorMessage );
        }

        var registrationBlocker = await license.GetRegistrationBlockerAsync( cancellationToken );

        if ( registrationBlocker != null )
        {
            return LicenseRegistrationResult.Failure( registrationBlocker );
        }

        var properties = registrationResult.Properties;

        if ( !dry )
        {
            this._configurationManager.Update<LicensingConfiguration>( config => config.SetLicense( properties, this._catalog ) );
        }

        return LicenseRegistrationResult.Success( properties );
    }

    public bool CanRegisterTrialEdition => this.CanRegisterTrialEditionCore( out _ );

    public void RemoveLicenses()
    {
        this._configurationManager.Update<LicensingConfiguration>( config => config.RemoveAllLicenses() );

        // A separate transaction, after the first one has released its lock: a transformation must not update another
        // configuration file. Without this, unregistering would leave the leases behind and the product would keep a
        // licence it was told to forget until that lease ended.
        this._leaseStore.RemoveAllLeases();
    }

    /// <inheritdoc />
    /// <remarks>
    /// A license server is reported with the properties of the licence it currently leases, so that the user sees
    /// which product the server gives them. No server is contacted: the stored lease is read, and a server that has
    /// not yet leased anything is reported with the little that is known about it.
    /// </remarks>
    public IEnumerable<LicenseRegistrationProperties> RegisteredLicenses
    {
        get
        {
            var configuration = this._configurationManager.Get<LicensingConfiguration>();

            foreach ( var licenseString in configuration.GetRegisteredLicenseStrings( this._currentVersion ) )
            {
                if ( LicenseServerUrl.IsLicenseServerUrl( licenseString ) )
                {
                    yield return this.GetLicenseServerProperties( licenseString );
                }
                else if ( LicenseKeyData.TryDeserialize( licenseString, out var licenseKeyData, out _ ) )
                {
                    yield return licenseKeyData.ToLicenseRegistrationProperties( this._catalog );
                }
            }
        }
    }

    /// <summary>
    /// Describes a registered license server from the lease it currently holds.
    /// </summary>
    private LicenseRegistrationProperties GetLicenseServerProperties( string licenseServerUrl )
    {
        if ( this._leaseStore.TryGetLease( licenseServerUrl, out var lease )
             && LicenseKeyData.TryDeserialize( lease.LicenseKey, out var leasedKeyData, out _ ) )
        {
            return LeasedLicense.ToLicenseServerProperties(
                leasedKeyData.ToLicenseRegistrationProperties( this._catalog, lease.LicenseKey ),
                licenseServerUrl,
                lease );
        }

        // No lease has been acquired yet, or the stored one cannot be read. The URL is all that is known, and
        // reporting nothing would make a registered server invisible to the user.
        return new LicenseRegistrationProperties(
            licenseServerUrl,
            licenseServerUrl,
            false,
            null,
            null,
            "License server",
            LicenseProduct.None,
            LicenseType.None,
            null,

            // No end date, so that the notification which warns about an expiring licence stays silent: a lease that
            // has not been acquired is not a licence about to expire.
            null,
            null,
            null,
            false,
            true,
            new Version( 5, 0, 22 ),
            LicenseGeneration.Current,
            ServicingPhase.Current ) { LicenseServerUrl = licenseServerUrl };
    }

    public IEnumerable<Version> UnsupportedRegisteredLicenseVersions
        => this._configurationManager.Get<LicensingConfiguration>().GetUnsupportedMinimalVersions( this._currentVersion );

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged( [CallerMemberName] string? propertyName = null )
    {
        this.PropertyChanged?.Invoke( this, new PropertyChangedEventArgs( propertyName ) );
    }
}