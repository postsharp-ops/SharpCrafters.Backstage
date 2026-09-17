// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using JetBrains.Annotations;
using System;

namespace SharpCrafters.Backstage.Licensing.LicenseServer;

/// <summary>
/// How the product reacts when a license server is configured over an insecure <c>http://</c> URL, which transmits
/// the user name and the machine name in cleartext.
/// </summary>
/// <remarks>
/// The lease request carries the name of the user and the name of the machine, so an <c>http://</c> server discloses
/// who works where to anyone on the path. Ported from PostSharp, where the same policy is message <c>PS0305</c>.
/// </remarks>
[PublicAPI]
public enum InsecureLicenseServerHandling
{
    /// <summary>
    /// Report a warning and use the license server anyway. This is the default.
    /// </summary>
    Warning,

    /// <summary>
    /// Report an error and do not use the license server, so that nothing is transmitted in cleartext.
    /// </summary>
    Error,

    /// <summary>
    /// Report nothing, for the administrator who runs the server on a network they consider safe.
    /// </summary>
    Allow
}

/// <summary>
/// Reads the value of the setting that chooses an <see cref="InsecureLicenseServerHandling"/>.
/// </summary>
[PublicAPI]
public static class InsecureLicenseServerHandlingParser
{
    /// <summary>
    /// The name of the setting, without the prefix of the product. It is read from the MSBuild property of the same
    /// name and, failing that, from the environment variable that the product profile names.
    /// </summary>
    public const string SettingName = "ALLOW_INSECURE_LICENSE_SERVER";

    /// <summary>
    /// Parses the value of the setting.
    /// </summary>
    /// <param name="value">The value of the setting, which may be <see langword="null"/>.</param>
    /// <returns>The handling that <paramref name="value"/> selects.</returns>
    /// <remarks>
    /// The synonyms are those of PostSharp, so that a build script that already sets the equivalent property keeps
    /// working: <c>True</c> means <see cref="InsecureLicenseServerHandling.Allow"/> and <c>False</c> means
    /// <see cref="InsecureLicenseServerHandling.Error"/>. A blank or unrecognized value selects the default, because
    /// a mistyped setting must not silently disable the check nor fail the build.
    /// </remarks>
    public static InsecureLicenseServerHandling Parse( string? value )
    {
        if ( string.IsNullOrWhiteSpace( value ) )
        {
            return InsecureLicenseServerHandling.Warning;
        }

        // ReSharper disable once RedundantSuppressNullableWarningExpression
        switch ( value!.Trim().ToLowerInvariant() )
        {
            case "allow":
            case "true":
                return InsecureLicenseServerHandling.Allow;

            case "error":
            case "false":
                return InsecureLicenseServerHandling.Error;

            default:
                return InsecureLicenseServerHandling.Warning;
        }
    }

    /// <summary>
    /// Determines whether a license server URL is insecure, that is, whether it transmits the user name and the
    /// machine name in cleartext.
    /// </summary>
    /// <param name="licenseServerUrl">The URL of a license server.</param>
    /// <remarks>
    /// A loopback address is not exempt. A server on the local machine is genuinely safe, but an exemption would also
    /// cover a loopback port forwarded to a remote host, which is not. An administrator who wants it uses
    /// <see cref="InsecureLicenseServerHandling.Allow"/>.
    /// </remarks>
    public static bool IsInsecure( string licenseServerUrl )
        => Uri.TryCreate( licenseServerUrl, UriKind.Absolute, out var uri ) && uri.Scheme == Uri.UriSchemeHttp;
}
