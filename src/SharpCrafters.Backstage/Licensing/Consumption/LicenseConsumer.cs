// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Application;
using SharpCrafters.Backstage.Diagnostics;
using SharpCrafters.Backstage.Extensibility;
using SharpCrafters.Backstage.Infrastructure;
using SharpCrafters.Backstage.Licensing.Licenses;
using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SharpCrafters.Backstage.Licensing.Consumption;

// The consumer holds a semaphore but is not disposable: it has the lifetime of a compilation, nothing signals its end,
// and a SemaphoreSlim that is never waited on with a timeout holds no handle to release.
#pragma warning disable CA1001
internal sealed class LicenseConsumer : ILicenseConsumer
{
    private readonly LicenseConsumptionOptions _options;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly ILogger _logger;
    private readonly IApplicationInfo _applicationInfo;
    private readonly IEventDispatcher _eventDispatcher;
    private readonly ProductProfile _productProfile;
    private readonly ILicenseProductCatalog _catalog;

    /// <summary>
    /// Acquires the licences that are only obtained when they are about to be used, which today means the licences
    /// leased from a license server.
    /// </summary>
    private readonly OnDemandLicenseAcquirer? _acquireOnDemandLicensesAsync;

    // A semaphore rather than a lock, because the acquisition waits for a network and a lock cannot be held across an
    // await.
    private readonly SemaphoreSlim _acquisitionSemaphore = new( 1, 1 );

    /// <summary>
    /// The licences available to this consumer. Licences acquired on demand are appended the first time a requirement
    /// needs them, so that they are acquired at most once per consumer.
    /// </summary>
    private ImmutableArray<(ILicense License, LicenseConsumptionProperties Properties)> _licenses;

    private bool _areOnDemandLicensesAcquired;

    internal delegate ValueTask<ImmutableArray<(ILicense License, LicenseConsumptionProperties Properties)>> OnDemandLicenseAcquirer(
        Action<LicensingMessage>? reportMessage,
        CancellationToken cancellationToken );

    internal LicenseConsumer(
        IServiceProvider services,
        ImmutableArray<(ILicense License, LicenseConsumptionProperties Properties)> licenses,
        LicenseConsumptionOptions options,
        OnDemandLicenseAcquirer? acquireOnDemandLicensesAsync = null )
    {
        this._licenses = licenses;
        this._options = options;
        this._acquireOnDemandLicensesAsync = acquireOnDemandLicensesAsync;
        this._areOnDemandLicensesAcquired = acquireOnDemandLicensesAsync == null;
        this._logger = services.GetLoggerFactory().Licensing();
        this._dateTimeProvider = services.GetRequiredBackstageService<IDateTimeProvider>();
        this._applicationInfo = services.GetRequiredBackstageService<IApplicationInfoProvider>().CurrentApplication;
        this._eventDispatcher = services.GetRequiredBackstageService<IEventDispatcher>();
        this._productProfile = services.GetRequiredBackstageService<ProductProfile>();
        this._catalog = services.GetRequiredBackstageService<ILicenseProductCatalog>();
    }

    /// <inheritdoc />
    public async ValueTask<bool> TryConsumeAsync(
        LicenseRequirement requirement,
        Action<LicensingMessage>? reportMessage = null,
        bool showsToastNotification = true,
        CancellationToken cancellationToken = default )
    {
        this._logger.Trace?.Log( $"TryConsume({{{requirement}}}" );

        var licenses = this._licenses;

        if ( this.TryConsumeCore( requirement, licenses, reportMessage ) )
        {
            return true;
        }

        // Nothing already available satisfies the requirement, so it is now worth acquiring the licences that are only
        // acquired when they are about to be used. What the server will lease is unknown until it is asked, so this
        // may take a seat for a licence that turns out not to satisfy the requirement either; asking is the only way
        // to find out, and the seat belongs to the user for the whole lease rather than to this build.
        if ( !this._areOnDemandLicensesAcquired )
        {
            var acquiredLicenses = await this.AcquireOnDemandLicensesAsync( reportMessage, cancellationToken );

            if ( !acquiredLicenses.IsEmpty && this.TryConsumeCore( requirement, acquiredLicenses, reportMessage ) )
            {
                return true;
            }

            licenses = this._licenses;
        }

        this._logger.Warning?.Log( $"TryConsume({{{requirement}}}: no eligible license found." );

        var messageText =
            $"The component '{requirement.ComponentNameWithServicingPhase}' is not licensed. It requires one of the following products: "
            + string.Join( ", ", requirement.GetEligibleProductNames( this._catalog ) )
            + ".";

        if ( licenses.IsEmpty )
        {
            messageText += " Could not find any valid registered license.";
        }
        else
        {
            messageText +=
                $" {licenses.Length} license keys were considered, but none was eligible: {string.Join( "; ", licenses.Select( x => x.Properties.LicenseString ) )}.";
        }

        // Report a licensing message (this is typically reported as a compiler diagnostic).
        reportMessage?.Invoke( new LicensingMessage( messageText ) { IsError = true } );

        // Publish the event, so that the user interface can show a notification, unless the application provides its own UI.
        if ( showsToastNotification )
        {
            this._eventDispatcher.Publish( new LicenseRequirementNotSatisfiedEvent( requirement, messageText ) );
        }

        return false;
    }

    /// <inheritdoc />
    [Obsolete( "Use TryConsumeAsync." )]
    public bool TryConsume( LicenseRequirement requirement, Action<LicensingMessage>? reportMessage = null, bool showsToastNotification = true )

        // Task.Run puts the continuation on a thread-pool thread, where SynchronizationContext.Current is null, so it
        // cannot be posted back to the thread that is blocked here, which is what deadlocks a user interface thread on
        // .NET Framework. GetResult rethrows the original exception, whereas Wait would wrap it in an
        // AggregateException whose message does not name the failure.
        => Task.Run( () => this.TryConsumeAsync( requirement, reportMessage, showsToastNotification, CancellationToken.None ).AsTask() )
            .GetAwaiter()
            .GetResult();

    /// <summary>
    /// Looks for a licence of a given set that satisfies a requirement, and reports its use when one does.
    /// </summary>
    private bool TryConsumeCore(
        LicenseRequirement requirement,
        ImmutableArray<(ILicense License, LicenseConsumptionProperties Properties)> licenses,
        Action<LicensingMessage>? reportMessage )
    {
        foreach ( var license in licenses )
        {
            // Check project-bound license keys.
            if ( !string.IsNullOrEmpty( license.Properties.LicensedNamespace )
                 && (string.IsNullOrEmpty( this._options.ProjectName ) || !this._options.ProjectName!.StartsWith(
                     license.Properties.LicensedNamespace!,
                     StringComparison.OrdinalIgnoreCase )) )
            {
                reportMessage?.Invoke(
                    new LicensingMessage(
                        $"The license key '{license.Properties.DisplayName}' is bound to the " +
                        $"'{license.Properties.LicensedNamespace}' namespace, but current project name is '{this._options.ProjectName}'." ) );

                this._logger.Warning?.Log(
                    $"TryConsume({{{requirement}}}: license key '{license.Properties.DisplayName}' ignored because it is bound to the namespace" +
                    $" '{license.Properties.LicensedNamespace}' it does not match the current project name '{this._options.ProjectName}'." );

                continue;
            }

            // Check eligibility.
            if ( requirement.IsEligible(
                    new LicenseConsumptionContext( license.Properties, this._applicationInfo, this._productProfile, this._dateTimeProvider.UtcNow, this._logger ) ) )
            {
                this._logger.Trace?.Log( $"TryConsume({{{requirement}}}: '{license.Properties.DisplayName}' is eligible." );

                license.License.ReportUse();

                return true;
            }
            else
            {
                this._logger.Trace?.Log( $"TryConsume({{{requirement}}}: '{license.Properties.DisplayName}' is not eligible." );
            }
        }

        return false;
    }

    /// <summary>
    /// Acquires the licences that are only acquired when they are about to be used, at most once per consumer, and
    /// appends them to the licences of this consumer.
    /// </summary>
    /// <returns>The licences that were acquired, which is empty when they had already been acquired.</returns>
    private async ValueTask<ImmutableArray<(ILicense License, LicenseConsumptionProperties Properties)>> AcquireOnDemandLicensesAsync(
        Action<LicensingMessage>? reportMessage,
        CancellationToken cancellationToken )
    {
        // One acquisition per consumer, whichever requirement asks for it first and however many ask at once. A
        // semaphore rather than a lock, because the acquisition waits for a network and a lock cannot be held across
        // an await.
        await this._acquisitionSemaphore.WaitAsync( cancellationToken ).ConfigureAwait( false );

        try
        {
            if ( this._areOnDemandLicensesAcquired )
            {
                return ImmutableArray<(ILicense, LicenseConsumptionProperties)>.Empty;
            }

            this._logger.Trace?.Log( "No available license satisfies the requirement. Acquiring the licenses that are acquired on demand." );

            var acquiredLicenses = await this._acquireOnDemandLicensesAsync!( reportMessage, cancellationToken );

            this._licenses = this._licenses.AddRange( acquiredLicenses );
            this._areOnDemandLicensesAcquired = true;

            return acquiredLicenses;
        }
        finally
        {
            this._acquisitionSemaphore.Release();
        }
    }
}
#pragma warning restore CA1001
