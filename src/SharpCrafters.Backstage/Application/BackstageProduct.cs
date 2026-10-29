// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using JetBrains.Annotations;
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
    /// for a composite product, which replaces the services that have to answer for every product it holds.
    /// </summary>
    internal Action<ServiceProviderBuilder, BackstageInitializationOptions>? OverrideServices { get; init; }

    /// <summary>
    /// Creates a product that hosts the Backstage services of several product families in one process, for instance
    /// Metalama and PostSharp in the Visual Studio extension that serves both.
    /// </summary>
    /// <param name="primary">The product whose profile, web links, telemetry endpoints, user interface addresses and
    /// configuration store the composite product uses.</param>
    /// <param name="others">The other products. Each one keeps its own configuration store, so its registered
    /// licenses and its telemetry consents stay where its own tools read them.</param>
    /// <returns>A product that can be passed to <see cref="BackstageInitializationOptions"/> like any other.</returns>
    /// <remarks>
    /// <para>
    /// The service provider created for the composite product is the service provider of <paramref name="primary"/>:
    /// there is one logger, one telemetry pipeline, and one set of notifications, welcome page and RSS client. Its
    /// <see cref="LicenseProductCatalog"/> is the union of the catalogs of all products.
    /// </para>
    /// <para>
    /// The provider creates, when it first needs it, one inner service provider for each of <paramref name="others"/>.
    /// An inner provider registers neither the user interface, the notifications, the welcome page, the RSS client,
    /// the crash dump service nor the tools extractor, and it uses the same instances as the outer provider for the
    /// services that do not depend on the product, such as the logger factory, the clock, the HTTP client factory and
    /// the event dispatcher. Disposing the outer provider disposes the inner providers.
    /// </para>
    /// <para>
    /// The following services of the outer provider answer for all products:
    /// </para>
    /// <list type="bullet">
    /// <item><description><see cref="Licensing.Registration.ILicenseRegistrationService"/> reports the licenses and the
    /// editions of every product. It registers a license key in the store of the product family to which the product of
    /// the key is native (see <see cref="ILicenseProductCatalog.IsNativeProduct"/>), so a PostSharp Framework or
    /// PostSharp Ultimate key, which Metalama also accepts, is stored by PostSharp. It registers a license server in
    /// the store of <paramref name="primary"/>, and removes the licenses of every store.</description></item>
    /// <item><description><see cref="Licensing.Consumption.ILicenseConsumptionService"/> creates a consumer that
    /// satisfies a requirement when the licenses of any product satisfy it.</description></item>
    /// <item><description><see cref="Telemetry.ITelemetryConfigurationService"/> reports the consents of
    /// <paramref name="primary"/> and writes a consent to every product.</description></item>
    /// <item><description><see cref="UserInterface.IIdeExtensionStatusService"/> writes the installation status of
    /// the extension to every product.</description></item>
    /// </list>
    /// </remarks>
    /// <exception cref="ArgumentException">A product is a composite product, or the same product is given twice.</exception>
    public static BackstageProduct CreateComposite( BackstageProduct primary, params BackstageProduct[] others )
        => CompositeBackstageProduct.Create( primary, others );
}
