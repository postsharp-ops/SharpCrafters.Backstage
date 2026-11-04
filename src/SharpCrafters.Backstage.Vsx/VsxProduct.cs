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
/// The product of the Visual Studio Tools for PostSharp and Metalama, which hosts the Backstage services of Metalama and
/// PostSharp in one process.
/// </summary>
/// <remarks>
/// <para>
/// The extension is a product of its own for its usage and exception reports, which it sends under the name of
/// <see cref="Profile"/> and to its own analytics site. It keeps the configuration of Metalama, so its consents, its
/// notifications and its device identifier are those of Metalama.
/// </para>
/// <para>
/// It is a composite product (see <see cref="BackstageProduct.CreateComposite"/>) whose primary member is Metalama. The
/// outer service provider holds the services of Metalama, and an inner service provider holds those of PostSharp.
/// </para>
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
    public static BackstageProduct Instance { get; } = BackstageProduct.CreateComposite(
        MetalamaProduct.Instance with { Profile = Profile, TelemetryOptions = TelemetryOptions },
        MetalamaProduct.Instance,
        PostSharpProduct.Instance );
}
