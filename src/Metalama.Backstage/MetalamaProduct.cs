// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using JetBrains.Annotations;
using Metalama.Backstage.Licensing;
using Metalama.Backstage.UserInterface;
using SharpCrafters.Backstage.Application;
using SharpCrafters.Backstage.Configuration;
using SharpCrafters.Backstage.Extensibility;
using SharpCrafters.Backstage.Licensing;
using SharpCrafters.Backstage.Licensing.Audit;
using SharpCrafters.Backstage.Telemetry;
using SharpCrafters.Backstage.UserInterface;
using System;

namespace Metalama.Backstage;

/// <summary>
/// The values that configure the Backstage services for the Metalama product family: the product profile, the web
/// links, the telemetry endpoints and the news feeds.
/// </summary>
/// <remarks>
/// This class is the default of the corresponding properties of <see cref="BackstageInitializationOptions"/>.
/// It belongs to the Metalama product package and not to the product-neutral services. A host of another product
/// supplies its own values.
/// </remarks>
[PublicAPI]
public static class MetalamaProduct
{
    /// <summary>
    /// The address of the RSS feed of the short Metalama news.
    /// </summary>
    /// <remarks>
    /// On the other product's host, which is not a mistake: one site serves both products, and this is the address
    /// the feed gives for itself. <c>metalama.net/briefs.xml</c> reaches the same file, but only because that one
    /// path is mapped to it, and the neighbouring <c>metalama.net/feed.xml</c> is not mapped the same way. Naming the
    /// file rather than the alias makes the two feeds of this product, and the two products, agree.
    /// </remarks>
    public const string BriefsFeedUrl = "https://postsharp.net/metalama/briefs.xml";

    /// <summary>
    /// The address of the RSS feed of the Metalama articles.
    /// </summary>
    /// <remarks>
    /// The posts of both products live in one blog, and this is the view of it that carries the posts categorized for
    /// Metalama. The merged feed stays at <c>postsharp.net/feed.xml</c> for whoever else reads it. See
    /// metalama/Metalama#2040.
    /// </remarks>
    public const string PostsFeedUrl = "https://postsharp.net/metalama/feed.xml";

    /// <summary>
    /// Gets the profile of the Metalama product family. Its values are the names that every version of Metalama has
    /// used on the machine, so they must not change.
    /// </summary>
    public static ProductProfile Profile { get; } = new(
        Name: "Metalama",
        Company: "PostSharp Technologies",
        DataDirectoryName: "Metalama",
        EnvironmentVariablePrefix: "METALAMA_",
        GlobalLockNamePrefix: "Global\\Metalama_",
        LicensePropertyName: "MetalamaLicense",
        AssemblyNamePrefix: "Metalama" )
    {
        LongName = "Metalama by PostSharp",
        LegacyDataDirectoryName = ".metalama",
        RepositoryConfigurationFileName = "metalama.json",
        HasLegacyConfigurationLock = true,
        ToolAssemblyNamePrefix = "Metalama.Backstage",
        CommandLineToolName = "metalama",
        LogoName = "metalama"
    };

    /// <summary>
    /// Gets the catalog of the products whose license keys Metalama consumes.
    /// </summary>
    public static ILicenseProductCatalog LicenseProductCatalog { get; } = MetalamaLicenseProductCatalog.Instance;

    /// <summary>
    /// Gets the web links of Metalama.
    /// </summary>
    public static IWebLinks WebLinks { get; } = new WebLinks();

    /// <summary>
    /// Gets the telemetry endpoints of Metalama.
    /// </summary>
    public static TelemetryInitializationOptions TelemetryOptions { get; } = new(
        new Uri( "https://bits.postsharp.net:44301/upload" ) )
    {
        AnalyticsUri = new Uri( "https://postsharp.matomo.cloud/matomo.php?idsite=6" )
    };

    /// <summary>
    /// Gets the user interface addresses of Metalama.
    /// </summary>
    public static UserInterfaceInitializationOptions UserInterfaceOptions { get; } = new()
    {
        BriefsFeedUrl = BriefsFeedUrl, PostsFeedUrl = PostsFeedUrl
    };

    /// <summary>
    /// Gets the Metalama product family, which binds the Backstage services to the values above.
    /// </summary>
    public static BackstageProduct Instance { get; } = new( Profile, WebLinks, TelemetryOptions, UserInterfaceOptions, LicenseProductCatalog )
    {
        RegisterServices = services =>
        {
            // Metalama shares nothing with an earlier version of itself through a store of the operating system, so
            // its configurations are files of its own.
            services.AddConfigurationServices();

            // An audit is throttled by the content of its report, so that a report is sent again whenever anything in
            // it changes. Nothing else reads this record, so there is no other version to agree with.
            services.AddService( typeof(ILicenseAuditKeyProvider), _ => ReportContentLicenseAuditKeyProvider.Instance );
        }
    };
}
