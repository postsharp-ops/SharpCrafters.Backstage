// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Application;
using SharpCrafters.Backstage.Diagnostics;
using SharpCrafters.Backstage.Extensibility;
using SharpCrafters.Backstage.Infrastructure;
using SharpCrafters.Backstage.Licensing.Licenses;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace SharpCrafters.Backstage.Licensing.Consumption;

internal sealed class LicenseConsumer : ILicenseConsumer
{
    private readonly ImmutableArray<(ILicense License, LicenseConsumptionProperties Properties)> _licenses;
    private readonly LicenseConsumptionOptions _options;
    private readonly Lazy<ImmutableArray<string>> _additionalProjectNames;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly ILogger _logger;
    private readonly IApplicationInfo _applicationInfo;
    private readonly IEventDispatcher _eventDispatcher;
    private readonly ProductProfile _productProfile;
    private readonly ILicenseProductCatalog _catalog;

    internal LicenseConsumer(
        IServiceProvider services,
        ImmutableArray<(ILicense License, LicenseConsumptionProperties Properties)> licenses,
        LicenseConsumptionOptions options )
    {
        this._licenses = licenses;
        this._options = options;
        this._additionalProjectNames = new Lazy<ImmutableArray<string>>( () => options.AdditionalProjectNamesProvider?.Invoke() ?? default );
        this._logger = services.GetLoggerFactory().Licensing();
        this._dateTimeProvider = services.GetRequiredBackstageService<IDateTimeProvider>();
        this._applicationInfo = services.GetRequiredBackstageService<IApplicationInfoProvider>().Application;
        this._eventDispatcher = services.GetRequiredBackstageService<IEventDispatcher>();
        this._productProfile = services.GetRequiredBackstageService<ProductProfile>();
        this._catalog = services.GetRequiredBackstageService<ILicenseProductCatalog>();
        this.Licenses = licenses.Select( x => x.Properties ).ToImmutableArray();
    }

    /// <inheritdoc />
    public ImmutableArray<LicenseConsumptionProperties> Licenses { get; }

    /// <inheritdoc />
    public bool TryConsume( LicenseRequirement requirement, Action<LicensingMessage>? reportMessage = null, bool showsToastNotification = true )
    {
        this._logger.Trace?.Log( $"TryConsume({{{requirement}}}" );

        if ( this.TryConsumeCore( requirement, reportMessage ) )
        {
            return true;
        }

        this._logger.Warning?.Log( $"TryConsume({{{requirement}}}: no eligible license found." );

        ReportRequirementNotSatisfied( requirement, this._catalog, this.Licenses, this._eventDispatcher, reportMessage, showsToastNotification );

        return false;
    }

    /// <summary>
    /// Reports that no license satisfies a requirement: it reports a licensing message and, unless the application
    /// provides its own user interface, publishes the event that shows a notification.
    /// </summary>
    /// <param name="requirement">The requirement that is not satisfied.</param>
    /// <param name="catalog">The catalog that names the products that would satisfy the requirement.</param>
    /// <param name="licenses">The licenses that were considered.</param>
    /// <param name="eventDispatcher">The dispatcher of the event.</param>
    /// <param name="reportMessage">A delegate that receives the message.</param>
    /// <param name="showsToastNotification">Whether to publish the event.</param>
    internal static void ReportRequirementNotSatisfied(
        LicenseRequirement requirement,
        ILicenseProductCatalog catalog,
        ImmutableArray<LicenseConsumptionProperties> licenses,
        IEventDispatcher eventDispatcher,
        Action<LicensingMessage>? reportMessage,
        bool showsToastNotification )
    {
        var messageText =
            $"The component '{requirement.ComponentNameWithServicingPhase}' is not licensed. It requires one of the following products: "
            + string.Join( ", ", requirement.GetEligibleProductNames( catalog ) )
            + ".";

        LicensingMessageKind messageKind;

        if ( licenses.IsEmpty )
        {
            messageText += " Could not find any valid registered license.";

            // The user is asked to register a license, and not told that the one they hold is not enough, so this is
            // a different message from the one below and an application reports it under a different diagnostic.
            messageKind = LicensingMessageKind.NoLicense;
        }
        else
        {
            // The licenses are named by their display name, which is the product and the identifier of the
            // license, and never by their string. This message is reported as a compiler diagnostic, so it
            // reaches build logs, continuous integration output and bug reports, and a license key is a
            // credential: naming the keys here would disclose every key the user holds to everyone who can
            // read a build log.
            messageText +=
                $" {licenses.Length} license keys were considered, but none was eligible: {string.Join( "; ", licenses.Select( x => x.DisplayName ) )}.";

            messageKind = LicensingMessageKind.RequirementNotSatisfied;
        }

        // Report a licensing message (this is typically reported as a compiler diagnostic).
        reportMessage?.Invoke( new LicensingMessage( messageText, messageKind ) { IsError = true } );

        // Publish the event, so that the user interface can show a notification, unless the application provides its own UI.
        if ( showsToastNotification )
        {
            eventDispatcher.Publish( new LicenseRequirementNotSatisfiedEvent( requirement, messageText ) );
        }
    }

    /// <summary>
    /// Gets the names under which the project may be licensed: the name of the project, and whatever further names
    /// the application offered for it.
    /// </summary>
    private IEnumerable<string> GetProjectNames()
    {
        if ( !string.IsNullOrEmpty( this._options.ProjectName ) )
        {
            yield return this._options.ProjectName!;
        }

        var additionalProjectNames = this._additionalProjectNames.Value;

        if ( !additionalProjectNames.IsDefault )
        {
            foreach ( var projectName in additionalProjectNames )
            {
                if ( !string.IsNullOrEmpty( projectName ) )
                {
                    yield return projectName;
                }
            }
        }
    }

    /// <summary>
    /// Determines whether the project is inside the namespace that a license key is constrained to. One of its names
    /// being inside it is enough: they are names of one project, and a key sold for an organization names that
    /// organization, not the way each of its projects happens to be named.
    /// </summary>
    private bool IsProjectInNamespace( string licensedNamespace )
        => this.GetProjectNames().Any( projectName => projectName.StartsWith( licensedNamespace, StringComparison.OrdinalIgnoreCase ) );

    /// <summary>
    /// Looks for a licence that satisfies a requirement, and reports its use when one does.
    /// </summary>
    /// <summary>
    /// Attempts to consume a license, without reporting that the requirement is not satisfied. The messages about the
    /// licenses that were skipped are still reported.
    /// </summary>
    internal bool TryConsumeCore( LicenseRequirement requirement, Action<LicensingMessage>? reportMessage )
    {
        foreach ( var license in this._licenses )
        {
            // Check project-bound license keys.
            if ( !string.IsNullOrEmpty( license.Properties.LicensedNamespace ) && !this.IsProjectInNamespace( license.Properties.LicensedNamespace! ) )
            {
                var projectNames = string.Join( "', '", this.GetProjectNames() );

                reportMessage?.Invoke(
                    new LicensingMessage(
                        $"The license key '{license.Properties.DisplayName}' is bound to the " +
                        $"'{license.Properties.LicensedNamespace}' namespace, but current project name is '{projectNames}'.",
                        LicensingMessageKind.NamespaceMismatch ) );

                this._logger.Warning?.Log(
                    $"TryConsume({{{requirement}}}: license key '{license.Properties.DisplayName}' ignored because it is bound to the namespace" +
                    $" '{license.Properties.LicensedNamespace}' it does not match the current project name '{projectNames}'." );

                continue;
            }

            // Check eligibility.
            if ( requirement.IsEligible(
                    new LicenseConsumptionContext(
                        license.Properties,
                        this._applicationInfo,
                        this._productProfile,
                        this._dateTimeProvider.UtcNow,
                        this._logger ) ) )
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
}