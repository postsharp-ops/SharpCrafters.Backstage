// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using JetBrains.Annotations;
using SharpCrafters.Backstage.Configuration;
using SharpCrafters.Backstage.Extensibility;
using System;

namespace SharpCrafters.Backstage.Licensing.LicenseServer;

/// <summary>
/// Warns that a license server is reached over an insecure <c>http://</c> URL.
/// </summary>
/// <remarks>
/// <para>
/// The warning is reported wherever a license server is used: when a license string becomes a licence, which is when
/// a build is about to lease from it, and when the user registers or tests a server, which is the moment they can
/// still choose a different URL.
/// </para>
/// <para>
/// It is a warning and never an error. PostSharp made it configurable as one of three behaviours, read from an
/// MSBuild property and an environment variable; refusing an <c>http://</c> server fails a build over a deployment
/// the developer did not choose and cannot change, so the only setting here is whether to say it at all.
/// </para>
/// </remarks>
[PublicAPI]
public static class InsecureLicenseServerWarning
{
    /// <summary>
    /// Gets the warning that a license string deserves, or <see langword="null"/> when it deserves none, which is the
    /// case for a license key, for an <c>https://</c> URL and for a user who has silenced the warning.
    /// </summary>
    /// <param name="services">The service provider, from which the licensing configuration is read.</param>
    /// <param name="licenseString">A license string, that is, a license key or a license server URL.</param>
    public static string? Get( IServiceProvider services, string? licenseString )
    {
        if ( !LicenseServerUrl.IsInsecure( licenseString ) )
        {
            return null;
        }

        if ( services.GetRequiredBackstageService<IConfigurationManager>().Get<LicensingConfiguration>().AllowInsecureLicenseServer )
        {
            return null;
        }

        return $"The license server '{licenseString}' is reached over HTTP, so the name of the user and the name of the machine are "
               + "transmitted in cleartext. Use an HTTPS URL, or set 'allowInsecureLicenseServer' to true in the 'licensing' "
               + "configuration to silence this warning.";
    }
}
