// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using JetBrains.Annotations;
using SharpCrafters.Backstage.Configuration;
using SharpCrafters.Backstage.Extensibility;
using System;
using System.Diagnostics.CodeAnalysis;

namespace SharpCrafters.Backstage.Licensing.LicenseServer;

/// <summary>
/// Decides whether the URL of a license server may be used, and whether using it deserves a warning.
/// </summary>
/// <remarks>
/// <para>
/// This is a service and not a static method because the warning depends on the configuration of the user, and
/// because the answer belongs to one place: the factory of licences, the registration service and the commands that
/// register or diagnose a server must all give the same reason for refusing the same URL.
/// </para>
/// <para>
/// <see cref="LicenseServerUrl"/> remains beside it and stays dependency-free. It answers the different question
/// "is this license string a URL rather than a license key?", which <see cref="LicensingConfiguration"/> asks from a
/// configuration record that has no service provider. This service is what composes that answer with the
/// configuration.
/// </para>
/// </remarks>
[PublicAPI]
public sealed class LicenseServerUrlValidator : IBackstageService
{
    private readonly IConfigurationManager _configurationManager;

    public LicenseServerUrlValidator( IServiceProvider serviceProvider )
    {
        this._configurationManager = serviceProvider.GetRequiredBackstageService<IConfigurationManager>();
    }

    /// <summary>
    /// Determines whether a license server URL may be used.
    /// </summary>
    /// <param name="licenseServerUrl">The URL of a license server, as the user supplied it.</param>
    /// <param name="errorMessage">The reason the URL cannot be used, when the method returns <see langword="false"/>.</param>
    /// <param name="warning">
    /// A warning that the URL may be used but deserves saying something about, or <see langword="null"/> when it
    /// deserves nothing. It is never a reason to refuse the URL.
    /// </param>
    /// <returns><see langword="true"/> when <paramref name="licenseServerUrl"/> may be used.</returns>
    /// <remarks>
    /// The only warning today is that the server is reached over HTTP, which transmits the name of the user and the
    /// name of the machine in cleartext. It is a warning and never an error: refusing the server would fail a build
    /// over a deployment the developer did not choose and cannot change. The user silences it by setting
    /// <see cref="LicensingConfiguration.AllowInsecureLicenseServer"/>.
    /// </remarks>
    public bool TryValidate( string? licenseServerUrl, [MaybeNullWhen( true )] out string errorMessage, out string? warning )
    {
        warning = null;

        if ( !LicenseServerUrl.IsLicenseServerUrl( licenseServerUrl, out errorMessage ) )
        {
            return false;
        }

        if ( LicenseServerUrl.IsInsecure( licenseServerUrl )
             && !this._configurationManager.Get<LicensingConfiguration>().AllowInsecureLicenseServer )
        {
            warning = $"The license server '{licenseServerUrl}' is reached over HTTP, so the name of the user and the name of the machine are "
                      + "transmitted in cleartext. Use an HTTPS URL, or set 'allowInsecureLicenseServer' to true in the 'licensing' "
                      + "configuration to silence this warning.";
        }

        return true;
    }
}
