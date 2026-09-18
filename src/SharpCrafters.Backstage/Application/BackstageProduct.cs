// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using JetBrains.Annotations;
using SharpCrafters.Backstage.Configuration.Registry;
using SharpCrafters.Backstage.Extensibility;
using SharpCrafters.Backstage.Licensing;
using SharpCrafters.Backstage.Telemetry;
using SharpCrafters.Backstage.UserInterface;
using System;
using System.Collections.Generic;

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
    /// A product owes two of them. It registers an <see cref="IConfigurationManager"/>, by calling either
    /// <see cref="RegisterConfigurationServices.AddConfigurationServices"/>, which keeps the configurations in files,
    /// or <see cref="RegisterConfigurationServices.AddRegistryConfigurationServices"/>, which keeps in the Windows
    /// registry the ones an <see cref="Configuration.Registry.IRegistryConfigurationSchemaProvider"/> names, so as to
    /// share them with the earlier versions of the product. And it registers an
    /// <see cref="Licensing.Audit.ILicenseAuditKeyProvider"/>, which says what makes two audits the same audit.
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
}
