// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using JetBrains.Annotations;
using PostSharp.Backstage.Configuration;
using SharpCrafters.Backstage.Application;
using SharpCrafters.Backstage.Configuration.Registry;
using SharpCrafters.Backstage.Extensibility;
using SharpCrafters.Backstage.Infrastructure;
using SharpCrafters.Backstage.Licensing;
using SharpCrafters.Backstage.Telemetry;
using SharpCrafters.Backstage.UserInterface;
using System;

namespace PostSharp.Backstage;

/// <summary>
/// The values that configure the Backstage services for the PostSharp product family: the product profile, the web
/// links, the telemetry endpoints and the news feeds.
/// </summary>
/// <remarks>
/// This class is the PostSharp counterpart of <c>MetalamaProduct</c>. It belongs to the PostSharp product package
/// and not to the product-neutral services, which are given these values through
/// <see cref="BackstageInitializationOptions"/>.
/// </remarks>
[PublicAPI]
public static class PostSharpProduct
{
    /// <summary>
    /// The address of the RSS feed of the PostSharp articles.
    /// </summary>
    /// <remarks>
    /// The posts of both products live in one blog, and this is the view of it that carries the posts categorized for
    /// PostSharp. The merged feed stays at <c>postsharp.net/feed.xml</c> for whoever else reads it, so the address of
    /// a product names the folder of that product rather than the root. See metalama/Metalama#2040.
    /// </remarks>
    public const string PostsFeedUrl = "https://postsharp.net/postsharp/feed.xml";

    /// <summary>
    /// The address of the RSS feed of the short PostSharp news.
    /// </summary>
    public const string BriefsFeedUrl = "https://postsharp.net/postsharp/briefs.xml";

    /// <summary>
    /// Gets the profile of the PostSharp product family. Its values are the names that every version of PostSharp has
    /// used on the machine, so they must not change.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="ProductProfile.EnvironmentVariablePrefix"/> is the prefix that PostSharp 2026.0 already uses, so
    /// that a variable such as <c>POSTSHARP_REGISTRY_ACCESS_DISABLED</c> keeps its name.
    /// <see cref="ProductProfile.LicensePropertyName"/> is the MSBuild property that PostSharp 2026.0 reads.
    /// </para>
    /// <para>
    /// <see cref="ProductProfile.DataDirectoryName"/> is <c>PostSharp</c> and not
    /// <c>SharpCrafters\PostSharp 3</c>, which is where PostSharp 2026.0 keeps its telemetry queue: the settings that
    /// the two versions share live in the registry, and nothing is gained by sharing a directory of derived files
    /// whose layout differs anyway.
    /// </para>
    /// </remarks>
    public static ProductProfile Profile { get; } = new(
        Name: "PostSharp",
        Company: "PostSharp Technologies",
        DataDirectoryName: "PostSharp",
        EnvironmentVariablePrefix: "POSTSHARP_",
        GlobalLockNamePrefix: "Global\\PostSharp_",
        LicensePropertyName: "PostSharpLicense",
        AssemblyNamePrefix: "PostSharp" )
    {
        LongName = "PostSharp",
        ToolAssemblyNamePrefix = "PostSharp.Backstage",
        LogoName = "postsharp",

        // PostSharp asks whether a license has been audited today, and not whether a report has been sent today,
        // which is what lets the record be shared with PostSharp 2026.0.
        LicenseAuditKeyProvider = PostSharpLicenseAuditKeyProvider.Instance
    };

    /// <summary>
    /// Gets the catalog of the products whose license keys PostSharp consumes.
    /// </summary>
    public static ILicenseProductCatalog LicenseProductCatalog { get; } = PostSharpLicenseProductCatalog.Instance;

    /// <summary>
    /// Gets the web links of PostSharp.
    /// </summary>
    public static IWebLinks WebLinks { get; } = new WebLinks();

    /// <summary>
    /// Gets the telemetry endpoints of PostSharp.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The upload address and the encryption key are those of the vendor and are shared with Metalama, because one
    /// service receives the packages of both products and one private key opens them.
    /// </para>
    /// <para>
    /// The analytics site is the one thing here that is specific to PostSharp: it is what files the aggregate usage
    /// and licence-audit pings under this product rather than under Metalama, which reports to site 6.
    /// </para>
    /// </remarks>
    public static TelemetryInitializationOptions TelemetryOptions { get; } = new( new Uri( "https://bits.postsharp.net:44301/upload" ) )
    {
        AnalyticsUri = new Uri( "https://postsharp.matomo.cloud/matomo.php?idsite=11" )
    };

    /// <summary>
    /// Gets the user interface addresses of PostSharp.
    /// </summary>
    public static UserInterfaceInitializationOptions UserInterfaceOptions { get; } =
        new() { PostsFeedUrl = PostsFeedUrl, BriefsFeedUrl = BriefsFeedUrl };

    /// <summary>
    /// Gets the PostSharp product family, which binds the Backstage services to the values above.
    /// </summary>
    public static BackstageProduct Instance { get; } = new( Profile, WebLinks, TelemetryOptions, UserInterfaceOptions, LicenseProductCatalog )
    {
        // PostSharp shares the registered licenses, the telemetry consents, the leases and the record of the audits
        // with PostSharp 2026.0, through the registry keys that version reads and writes.
        RegisterServices = services => services.AddService(
            typeof(IRegistryConfigurationSchemaProvider),
            serviceProvider => new PostSharpRegistryConfigurationSchemaProvider(
                serviceProvider.GetRequiredBackstageService<IDateTimeProvider>() ) )
    };
}
