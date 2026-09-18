// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using JetBrains.Annotations;
using SharpCrafters.Backstage.Configuration.Registry;
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
    /// Gets the factory of the schemas that map the configuration objects the product keeps in the Windows registry,
    /// or <see langword="null"/> when it keeps them all in files.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A product keeps a configuration object in the registry to share it with its earlier versions, which is what
    /// PostSharp does. Declaring the schemas here rather than at each entry point means that every host of the
    /// product reads and writes the same store: a host that forgot would quietly use files of its own and the two
    /// versions would stop sharing anything.
    /// </para>
    /// <para>
    /// It is a factory over the service provider because a schema may need a service — the licensing one needs the
    /// clock — and a product is described once, before any service exists.
    /// </para>
    /// </remarks>
    public Func<IServiceProvider, IEnumerable<IRegistryConfigurationSchema>>? CreateConfigurationSchemas { get; init; }
}
