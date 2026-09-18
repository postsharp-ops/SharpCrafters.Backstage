// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using JetBrains.Annotations;
using SharpCrafters.Backstage.Application;
using SharpCrafters.Backstage.Extensibility;
using SharpCrafters.Backstage.Licensing;
using SharpCrafters.Backstage.Telemetry;
using SharpCrafters.Backstage.UserInterface;
using System;
using System.IO;

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
    public const string PostsFeedUrl = "https://blog.postsharp.net/feed.xml";

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
        LogoName = "postsharp"
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
    /// The upload address and the encryption key are those of the vendor and are shared with Metalama, because one
    /// service receives the packages of both products. Only the analytics site identifier is specific to PostSharp.
    /// </remarks>
    public static TelemetryInitializationOptions TelemetryOptions { get; } = new(
        new Uri( "https://bits.postsharp.net:44301/upload" ),
        GetUploadEncryptionPublicKey )
    {
        AnalyticsUri = new Uri( "https://postsharp.matomo.cloud/matomo.php?idsite=1" )
    };

    /// <summary>
    /// Gets the user interface addresses of PostSharp.
    /// </summary>
    /// <remarks>
    /// <see cref="UserInterfaceInitializationOptions.BriefsFeedUrl"/> is left unset because PostSharp publishes no
    /// feed of short news, only the articles of its blog.
    /// </remarks>
    public static UserInterfaceInitializationOptions UserInterfaceOptions { get; } = new() { PostsFeedUrl = PostsFeedUrl };

    /// <summary>
    /// Gets the PostSharp product family, which binds the Backstage services to the values above.
    /// </summary>
    public static BackstageProduct Instance { get; } = new( Profile, WebLinks, TelemetryOptions, UserInterfaceOptions, LicenseProductCatalog );

    /// <summary>
    /// Reads the public key that encrypts the telemetry packages from the resources of the current assembly.
    /// </summary>
    /// <returns>The public key, in the <c>RSAKeyValue</c> XML format.</returns>
    private static byte[] GetUploadEncryptionPublicKey()
    {
        using var keyStream = typeof(PostSharpProduct).Assembly.GetManifestResourceStream( "PostSharp.Backstage.Telemetry.public.key" )
                              ?? throw new InvalidOperationException( "The public key that encrypts the telemetry packages was not found." );

        using var memoryStream = new MemoryStream();
        keyStream.CopyTo( memoryStream );

        return memoryStream.ToArray();
    }
}
