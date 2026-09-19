// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using JetBrains.Annotations;
using SharpCrafters.Backstage.Configuration;
using SharpCrafters.Backstage.Extensibility;
using SharpCrafters.Backstage.Infrastructure;
using SharpCrafters.Backstage.Licensing.Consumption;
using System;
using System.Collections.Generic;

namespace SharpCrafters.Backstage.Licensing.Registration;

/// <summary>
/// What a <see cref="SelfRegisteredEdition"/> is given when it is asked whether it can be registered, and when it
/// registers: the choices of the caller, the state of the machine, and the one operation that writes a license key.
/// </summary>
/// <remarks>
/// <para>
/// An edition must not keep the context beyond the call. An edition is created once per product family and a family is
/// described by a static, while a service belongs to a service provider, of which a process may hold several: the
/// worker registers the Backstage services a second time into its own container, so two configuration managers are
/// live at once. An edition that cached a context would write through whichever provider happened to call it first.
/// </para>
/// <para>
/// Only <see cref="ILicenseRegistrationService"/> creates one, so an edition can never be handed a context that has no
/// services behind it.
/// </para>
/// </remarks>
[PublicAPI]
public sealed class SelfRegisteredEditionContext
{
    private readonly IConfigurationManager _configurationManager;
    private readonly ILicenseRegistrationService _registrationService;

    internal SelfRegisteredEditionContext(
        IServiceProvider serviceProvider,
        ILicenseRegistrationService registrationService,
        SelfRegisteredEditionOptions options )
    {
        this.ServiceProvider = serviceProvider;
        this._registrationService = registrationService;
        this._configurationManager = serviceProvider.GetRequiredBackstageService<IConfigurationManager>();
        this.Catalog = serviceProvider.GetRequiredBackstageService<ILicenseProductCatalog>();

        // Read from the clock of the service provider and not from DateTime.UtcNow, so that a caller which drives a
        // clock of its own — every test of the trial period does — is obeyed.
        this.UtcNow = serviceProvider.GetRequiredBackstageService<IDateTimeProvider>().UtcNow;

        this.CommunityLicenseReason = options.CommunityLicenseReason;
        this.ReportMessage = options.ReportMessage;
    }

    /// <summary>
    /// Gets the services of the host.
    /// </summary>
    public IServiceProvider ServiceProvider { get; }

    /// <summary>
    /// Gets the catalog of the product family, which names its products.
    /// </summary>
    public ILicenseProductCatalog Catalog { get; }

    /// <summary>
    /// Gets the current moment, as the host counts it.
    /// </summary>
    public DateTime UtcNow { get; }

    /// <summary>
    /// Gets the reason the user says they are entitled to the edition.
    /// </summary>
    public CommunityLicenseReason CommunityLicenseReason { get; }

    /// <summary>
    /// Gets the sink of the messages that the registration reports, or <see langword="null"/> to discard them.
    /// </summary>
    public Action<LicensingMessage>? ReportMessage { get; }

    /// <summary>
    /// Gets the licensing configuration as it stands. Reading it takes no lock, so it is a snapshot and not a
    /// reservation: the value may be stale by the time a license is written.
    /// </summary>
    public LicensingConfiguration Configuration => this._configurationManager.Get<LicensingConfiguration>();

    /// <summary>
    /// Gets the licenses registered on this machine, which is what an availability rule reads.
    /// </summary>
    public IEnumerable<LicenseRegistrationProperties> RegisteredLicenses => this._registrationService.RegisteredLicenses;

    /// <summary>
    /// Reports a message that does not stop the registration.
    /// </summary>
    public void Report( string message ) => this.ReportMessage?.Invoke( new LicensingMessage( message ) );

    /// <summary>
    /// Builds the key of a license and registers it.
    /// </summary>
    /// <param name="license">What the key says.</param>
    /// <param name="configure">
    /// Records whatever else the edition keeps beside the key, or <see langword="null"/> when it keeps nothing.
    /// </param>
    /// <remarks>
    /// The key and <paramref name="configure"/> are written in one transaction, for two reasons. A transformation may
    /// not update a second configuration object, so two calls would throw; and were they allowed, a process that died
    /// between them would leave the key registered and the state beside it unwritten — for the trial, that is a trial
    /// whose cool-off period never started.
    /// </remarks>
    public LicenseRegistrationResult RegisterLicense(
        UnsignedLicense license,
        Func<LicensingConfiguration, LicensingConfiguration>? configure = null )
    {
        var properties = new UnsignedLicenseFactory( this.ServiceProvider ).CreateLicenseKey( license );

        var outcome = this._configurationManager.Update(
            typeof(LicensingConfiguration),
            current =>
            {
                var updated = ((LicensingConfiguration) current).SetLicense( properties, this.Catalog );

                return configure == null ? updated : configure( updated );
            } );

        return outcome switch
        {
            // The file already said exactly this, or the transformation declined. The edition is registered either
            // way, which is what was asked for.
            ConfigurationUpdateOutcome.Updated or ConfigurationUpdateOutcome.NoChange or ConfigurationUpdateOutcome.Declined =>
                LicenseRegistrationResult.Success( properties ),

            ConfigurationUpdateOutcome.LockTimeout => LicenseRegistrationResult.Failure(
                "The licensing configuration is being written by another process. Try again." ),

            _ => LicenseRegistrationResult.Failure( "The licensing configuration could not be written." )
        };
    }
}
