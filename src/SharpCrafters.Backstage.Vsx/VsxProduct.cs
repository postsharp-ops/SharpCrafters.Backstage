// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using JetBrains.Annotations;
using Metalama.Backstage;
using PostSharp.Backstage;
using SharpCrafters.Backstage.Application;
using SharpCrafters.Backstage.Telemetry;
using System;

namespace SharpCrafters.Backstage.Vsx;

/// <summary>
/// The product of the PostSharp and Metalama Visual Studio extension, which hosts the Backstage services of Metalama and
/// PostSharp in one process.
/// </summary>
/// <remarks>
/// <para>
/// The extension is a product of its own for its usage and exception reports, which it sends under the name of
/// <see cref="Profile"/>. It keeps the configuration of Metalama, so its consents, its notifications and its device
/// identifier are those of Metalama.
/// </para>
/// <para>
/// The service provider created for this product is the outer service provider. It holds the services of Metalama, the
/// primary member, and creates, when it first needs it, an inner service provider for PostSharp. The inner provider has
/// no user interface, notifications, welcome page, RSS client, crash dump service or tools extractor, and collects no
/// usage or exception telemetry. It uses the same instances as the outer provider for the services that do not depend on
/// the product, such as the logger factory, the clock, the HTTP client factory and the event dispatcher. Disposing the
/// outer provider disposes the inner provider.
/// </para>
/// <para>
/// Each product audits its own licenses: the audit report goes to the upload queue of the product and the analytics
/// event to the analytics site of the product. The extension uploads the queue of PostSharp in its own process.
/// </para>
/// <para>
/// The following services of the outer provider answer for both products:
/// </para>
/// <list type="bullet">
/// <item><description><see cref="Licensing.Registration.ILicenseRegistrationService"/> reports the licenses and the
/// editions of both products. It registers a license key in the store of the product family to which the product of the
/// key is native (see <see cref="Licensing.ILicenseProductCatalog.IsNativeProduct"/>), so a PostSharp Framework or
/// PostSharp Ultimate key, which Metalama also accepts, is stored by PostSharp. It registers a license server in the
/// store of Metalama, and removes the licenses of every store.</description></item>
/// <item><description><see cref="Licensing.Consumption.ILicenseConsumptionService"/> creates a consumer that satisfies a
/// requirement when the licenses of any product satisfy it.</description></item>
/// <item><description><see cref="ITelemetryConfigurationService"/> reports the maximal consent of both products and
/// writes a consent to both.</description></item>
/// <item><description><see cref="UserInterface.IIdeExtensionStatusService"/> keeps the installation status of the
/// extension once for all products, and also writes it to the configuration store of both products, where earlier
/// versions of the products read it.</description></item>
/// </list>
/// </remarks>
[PublicAPI]
public static class VsxProduct
{
    /// <summary>
    /// Gets the profile of the extension: the profile of Metalama, whose configuration the extension keeps, under the name
    /// of the extension.
    /// </summary>
    public static ProductProfile Profile { get; } = MetalamaProduct.Profile with
    {
        Name = "Visual Studio Tools for PostSharp and Metalama", LongName = "Visual Studio Tools for PostSharp and Metalama"
    };

    /// <summary>
    /// Gets the telemetry endpoints of the extension: the upload endpoint that Metalama and PostSharp share, and the
    /// analytics site of the extension.
    /// </summary>
    public static TelemetryInitializationOptions TelemetryOptions { get; } = MetalamaProduct.TelemetryOptions with
    {
        AnalyticsUri = new Uri( "https://postsharp.matomo.cloud/matomo.php?idsite=12" )
    };

    /// <summary>
    /// Gets the product of the extension, which can be passed to <see cref="Extensibility.BackstageInitializationOptions"/>
    /// like any other product.
    /// </summary>
    public static BackstageProduct Instance { get; } = CompositeBackstageProduct.Create(
        MetalamaProduct.Instance with { Profile = Profile, TelemetryOptions = TelemetryOptions },
        MetalamaProduct.Instance,
        PostSharpProduct.Instance );
}
