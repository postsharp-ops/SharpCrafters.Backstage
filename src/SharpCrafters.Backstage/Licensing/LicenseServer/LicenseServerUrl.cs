// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using System;
using System.Diagnostics.CodeAnalysis;

namespace SharpCrafters.Backstage.Licensing.LicenseServer;

/// <summary>
/// Recognizes the license strings that are the URL of a license server rather than a license key.
/// </summary>
/// <remarks>
/// This class depends on nothing, because the question "is this string a license server URL?" is asked by the
/// licensing configuration, by the license sources and by the registration service, none of which has any business
/// with HTTP or with a service provider.
/// </remarks>
internal static class LicenseServerUrl
{
    /// <summary>
    /// Determines whether a license string is the URL of a license server.
    /// </summary>
    /// <param name="licenseString">A license string, that is, a license key or a license server URL.</param>
    /// <returns><see langword="true"/> if <paramref name="licenseString"/> is a well-formed license server URL.</returns>
    public static bool IsLicenseServerUrl( string? licenseString ) => IsLicenseServerUrl( licenseString, out _ );

    /// <summary>
    /// Determines whether a license string is the URL of a license server, and gives the reason when it is not.
    /// </summary>
    /// <param name="licenseString">A license string, that is, a license key or a license server URL.</param>
    /// <param name="errorMessage">The reason why <paramref name="licenseString"/> is not a license server URL.</param>
    /// <returns><see langword="true"/> if <paramref name="licenseString"/> is a well-formed license server URL.</returns>
    /// <remarks>
    /// A caller that has a string which is not a license key uses <paramref name="errorMessage"/> to tell the user
    /// what is wrong with the URL they typed. A caller that is merely dispatching between a key and a URL ignores it,
    /// because a license key is not a URL and its rejection here means nothing.
    /// </remarks>
    public static bool IsLicenseServerUrl( string? licenseString, [MaybeNullWhen( true )] out string errorMessage )
    {
        if ( licenseString == null || !Uri.IsWellFormedUriString( licenseString, UriKind.Absolute ) )
        {
            errorMessage = "Invalid URL.";

            return false;
        }

        var uri = new Uri( licenseString, UriKind.Absolute );

        if ( uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps )
        {
            errorMessage = "Only HTTP and HTTPS are acceptable protocols.";

            return false;
        }

        if ( !string.IsNullOrEmpty( uri.Query ) )
        {
            // The client appends its own query string to this URL, so one that already has a query string would
            // produce a request with two of them.
            errorMessage = "The URL cannot contain a query string.";

            return false;
        }

        if ( !string.IsNullOrEmpty( uri.UserInfo ) )
        {
            // PostSharp accepted a URL with a user info because it used WebClient, which honoured it. HttpClient
            // ignores it silently, so accepting one would send an anonymous request while the user believes they
            // configured a credential, and would store that credential in a configuration file in clear text.
            errorMessage = "The URL cannot contain a user name or a password. Use a license server that accepts the credentials of the current user.";

            return false;
        }

        errorMessage = null;

        return true;
    }

    /// <summary>
    /// Gets the key under which the lease of a license server is stored, so that two registrations that differ only
    /// by a trailing slash or by the case of the host share one lease instead of leasing a seat each.
    /// </summary>
    /// <param name="licenseServerUrl">The URL of a license server, as it was registered.</param>
    /// <returns>The key of <paramref name="licenseServerUrl"/> in the lease store.</returns>
    /// <remarks>
    /// Only the key is normalized. The request is always built from the string the user registered, because the path
    /// of a URL is case-sensitive on most servers and must not be touched.
    /// </remarks>
    public static string GetStoreKey( string licenseServerUrl ) => licenseServerUrl.TrimEnd( '/' );
}
