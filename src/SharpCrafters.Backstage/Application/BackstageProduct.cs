// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using JetBrains.Annotations;
using SharpCrafters.Backstage.Application.Composite;
using SharpCrafters.Backstage.Extensibility;
using SharpCrafters.Backstage.Licensing;
using SharpCrafters.Backstage.Telemetry;
using SharpCrafters.Backstage.UserInterface;
using System;

namespace SharpCrafters.Backstage.Application;

/// <summary>
/// The values that bind the Backstage services to a product family: the profile, the web links, the telemetry
/// endpoints, the user interface addresses and the license product catalog. A product defines one instance, for
/// instance <c>MetalamaProduct.Instance</c>, and passes it wherever the services are composed.
/// </summary>
/// <param name="Profile">The profile of the product family.</param>
/// <param name="WebLinks">The web links of the product.</param>
/// <param name="TelemetryOptions">The telemetry endpoints of the product.</param>
/// <param name="UserInterfaceOptions">The user interface addresses of the product.</param>
/// <param name="LicenseProductCatalog">The catalog of the products whose license keys the product family consumes.</param>
[PublicAPI]
public sealed record BackstageProduct(
    ProductProfile Profile,
    IWebLinks WebLinks,
    TelemetryInitializationOptions TelemetryOptions,
    UserInterfaceInitializationOptions UserInterfaceOptions,
    ILicenseProductCatalog LicenseProductCatalog )
{
    /// <summary>
    /// Gets the services that the product contributes, which are every service this package does not answer for
    /// itself.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is where a product says the things only it knows, and nothing of it is assumed on its behalf: a default
    /// registered here would be one that a product could be given without having asked for it, and the product that
    /// wanted something else would be relying on the order of two registrations to overrule it.
    /// </para>
    /// <para>
    /// A product owes three of them. It registers an <see cref="Configuration.IConfigurationManager"/>, by calling
    /// either <see cref="Configuration.RegisterConfigurationServices.AddConfigurationServices"/>, which keeps the
    /// configurations in files, or
    /// <see cref="Configuration.RegisterConfigurationServices.AddRegistryConfigurationServices"/>, which keeps in the
    /// Windows registry the ones an <see cref="Configuration.Registry.IRegistryConfigurationSchemaProvider"/> names,
    /// so as to share them with the earlier versions of the product. It registers an
    /// <see cref="Licensing.Audit.ILicenseAuditKeyProvider"/>, which says what makes two audits the same audit. And it
    /// registers an <see cref="Telemetry.IUsageSessionStore"/>, by calling either
    /// <see cref="Telemetry.RegisterTelemetryServices.AddFileUsageSessionStore"/> or
    /// <see cref="Telemetry.RegisterTelemetryServices.AddRegistryUsageSessionStore"/>, which says where the time of the
    /// last usage report of each project is kept.
    /// </para>
    /// <para>
    /// Declaring them here rather than at each entry point means every host of the product gets the same answers: a
    /// host that forgot would read and write a store that the other version never looks at, and the two versions
    /// would silently stop sharing anything.
    /// </para>
    /// <para>
    /// It is an action over a builder rather than a list of instances because a service may need another service — a
    /// registry schema needs the clock — and a product is described once, before any service exists.
    /// </para>
    /// </remarks>
    public required Action<ServiceProviderBuilder> RegisterServices { get; init; }

    /// <summary>
    /// Gets an action that <see cref="RegisterServiceExtensions.AddBackstageServices"/> invokes after every other
    /// registration, so that the services it registers replace the standard ones. It is <see langword="null"/> except
    /// for a composite product (see <see cref="CreateComposite"/>), which replaces the services that have to answer for
    /// every product it holds.
    /// </summary>
    internal Action<ServiceProviderBuilder, BackstageInitializationOptions>? OverrideServices { get; init; }

    /// <summary>
    /// Gets a function that <see cref="RegisterServiceExtensions.AddBackstageServices"/> applies to the options before any
    /// registration, so that a product can overrule the choices of its host. It is <see langword="null"/> except for a
    /// composite product (see <see cref="CreateComposite"/>).
    /// </summary>
    internal Func<BackstageInitializationOptions, BackstageInitializationOptions>? AdjustOptions { get; init; }

    /// <summary>
    /// Creates a product that hosts the Backstage services of several product families in one process, for instance
    /// Metalama and PostSharp in the Visual Studio extension that serves both.
    /// </summary>
    /// <param name="host">The product whose profile, web links, telemetry endpoints, user interface addresses and
    /// license product catalog the composite product carries. The process reports its usage and its exceptions under
    /// the identity of the host. The host must keep the configuration store of the primary member.</param>
    /// <param name="members">The hosted products, the primary member first. Each one keeps its own configuration store,
    /// so its registered licenses and its telemetry consents stay where its own tools read them.</param>
    /// <returns>A product that can be passed to <see cref="BackstageInitializationOptions"/> like any other.</returns>
    /// <remarks>
    /// <para>
    /// The service provider created for the composite product is the outer service provider. It holds the services of
    /// the primary member, and runs the only logger and usage and exception telemetry of the process. It detects no
    /// notification and runs no RSS client, whatever the host asks, because the builds of each product show their own.
    /// It keeps the welcome page service but does not open the welcome page when telemetry is activated, because the
    /// extension opens a welcome page of its own.
    /// </para>
    /// <para>
    /// The outer provider creates, when it first needs it, one inner service provider for each other member. An inner
    /// provider has no user interface, notifications, welcome page, RSS client, crash dump service or tools extractor,
    /// and collects no usage or exception telemetry. It uses the same instances as the outer provider for the service
    /// types marked with <see cref="ProductNeutralServiceAttribute"/>. Disposing the outer provider disposes the inner
    /// providers.
    /// </para>
    /// <para>
    /// Each member audits its own licenses: the audit report goes to the upload queue of the member and the analytics
    /// event to the analytics site of the member. The process uploads the queue of each inner member itself.
    /// </para>
    /// <para>
    /// The following services of the outer provider answer for all members:
    /// </para>
    /// <list type="bullet">
    /// <item><description><see cref="Licensing.Registration.ILicenseRegistrationService"/> reports the licenses and the
    /// editions of every member, each license key once. It registers a license key in the store of every member whose
    /// family accepts it, the member to which it is native (see <see cref="ILicenseProductCatalog.IsNativeProduct"/>)
    /// first; a store that cannot register it is reported as an exception and does not fail the registration. It
    /// registers a license server in the store of the primary member, and removes the licenses of every
    /// store.</description></item>
    /// <item><description><see cref="Licensing.Consumption.ILicenseConsumptionService"/> creates a consumer that
    /// satisfies a requirement when the licenses of any member satisfy it. A key held by several members is consumed,
    /// and so audited, by the member to which it is native.</description></item>
    /// <item><description><see cref="UserInterface.Toasts.IToastNotificationStatusService"/> pauses the notifications of
    /// every member.</description></item>
    /// <item><description><see cref="Telemetry.ITelemetryConfigurationService"/> reports the maximal consent of the
    /// members and writes a consent to every member.</description></item>
    /// <item><description><see cref="UserInterface.IIdeExtensionStatusService"/> keeps the installation status of the
    /// extension once for all products, and also writes it to the configuration store of every member, where earlier
    /// versions of the products read it.</description></item>
    /// </list>
    /// </remarks>
    /// <exception cref="ArgumentException">A product is a composite product, the same product family is given twice, or
    /// the host does not keep the configuration store of the primary member.</exception>
    public static BackstageProduct CreateComposite( BackstageProduct host, params BackstageProduct[] members )
        => CompositeBackstageProduct.Create( host, members );
}
