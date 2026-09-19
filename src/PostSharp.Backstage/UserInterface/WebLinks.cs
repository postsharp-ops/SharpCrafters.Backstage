// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using JetBrains.Annotations;
using SharpCrafters.Backstage.UserInterface;

namespace PostSharp.Backstage;

#pragma warning disable CA1822

// ReSharper disable MemberCanBeMadeStatic.Global
/// <summary>
/// The web links of PostSharp. This is the implementation of <see cref="IWebLinks"/> for the PostSharp product family.
/// </summary>
/// <remarks>
/// Every address is an alias resolved by the website rather than a URL of a page, so that a page can move without a
/// released version of the product pointing at nothing.
/// </remarks>
[PublicAPI]
public sealed class WebLinks : IWebLinks
{
    public const string TrackingQueryString = "utm_source=app&utm_medium=app&utm_campaign=backstage";

    string IWebLinks.TrackingQueryString => TrackingQueryString;

    // We don't add campaign tracking query string parameters so we do not override the attribution to the original campaign.
    public string Welcome => GetLink( "postsharp-welcome", false );

    public string GetTeamTrial => GetLink( "postsharp-team-evaluation" );

    public string VisualStudioMarketplace => GetLink( "postsharp-download-vsx" );

    public string PrivacyPolicy => GetLink( "postsharp-privacy-policy" );

    public string LicenseAgreement => GetLink( "postsharp-license-agreement" );

    public string Documentation => GetLink( "postsharp-documentation" );

    public string InstallVsx => this.VisualStudioMarketplace;

    public string RenewSubscription => GetLink( "postsharp-renew-subscription" );

    public string DotNetTool => GetLink( "postsharp-dotnet_tool" );

    public string DisableTelemetryInstructions => GetLink( "postsharp-disable-telemetry" );

    public string NewsPosts => "https://blog.postsharp.net";

    /// <remarks>
    /// PostSharp publishes no feed of short news, so this is the same page as <see cref="NewsPosts"/>.
    /// </remarks>
    public string NewsBriefs => this.NewsPosts;

    private static string GetLink( string alias, bool trackCampaign = true, string? queryString = null )
    {
        var url = $"https://www.postsharp.net/links/{alias}";
        var queryStringSeparator = '?';

        if ( trackCampaign )
        {
            url += "?" + TrackingQueryString;
            queryStringSeparator = '&';
        }

        if ( !string.IsNullOrEmpty( queryString ) )
        {
            url += queryStringSeparator + queryString;
        }

        return url;
    }
}
