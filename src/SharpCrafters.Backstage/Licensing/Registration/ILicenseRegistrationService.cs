// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using JetBrains.Annotations;
using SharpCrafters.Backstage.Extensibility;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;

namespace SharpCrafters.Backstage.Licensing.Registration;

[PublicAPI]
public interface ILicenseRegistrationService : IBackstageService, INotifyPropertyChanged
{
    LicenseRegistrationResult RegisterCommunityEdition( CommunityLicenseReason reason );

    [Obsolete]
    LicenseRegistrationResult RegisterLegacyFreeEdition();

    LicenseRegistrationResult RegisterTrialEdition();

    /// <summary>
    /// Registers a license string, which is either a license key or the URL of a license server.
    /// </summary>
    /// <param name="licenseString">The license key, or the URL of a license server.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <remarks>
    /// Registering the URL of a license server contacts it, so that the user learns at once that the URL is wrong or
    /// that the server has no licence for them, rather than on their next build.
    /// </remarks>
    ValueTask<LicenseRegistrationResult> RegisterLicenseAsync( string licenseString, CancellationToken cancellationToken = default );

    /// <inheritdoc cref="RegisterLicenseAsync"/>
    [Obsolete( "Use RegisterLicenseAsync. This overload blocks the calling thread while a license server is contacted." )]
    LicenseRegistrationResult RegisterLicense( string licenseString );

    bool CanRegisterTrialEdition { get; }

    /// <summary>
    /// Removes every registered license key and license server, and the leases held from those servers.
    /// </summary>
    void RemoveLicenses();

    /// <summary>
    /// Gets the registered licences, which are the license keys and the license servers. A license server is reported
    /// with the properties of the licence it currently leases, if it holds one; no server is contacted.
    /// </summary>
    IEnumerable<LicenseRegistrationProperties> RegisteredLicenses { get; }

    /// <summary>
    /// Gets the minimal versions of the registered license keys that the running version of Metalama cannot consume,
    /// ordered by version. Such a license key is stored in a group named after the version, and the version is the
    /// only information about it that the running version has, because the license key itself is not deserialized.
    /// </summary>
    IEnumerable<Version> UnsupportedRegisteredLicenseVersions { get; }

    /// <summary>
    /// Validates a license string and returns a value indicating whether it can be registered using
    /// <see cref="RegisterLicenseAsync"/>, without registering it.
    /// </summary>
    ValueTask<LicenseRegistrationResult> ValidateLicenseKeyAsync( string licenseKey, CancellationToken cancellationToken = default );

    /// <summary>
    /// Contacts a license server and reports the licence it would lease, without registering anything.
    /// </summary>
    /// <param name="licenseServerUrl">The URL of the license server.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <remarks>
    /// Diagnosing an on-premises server is the most common support interaction for this feature, and registering is
    /// the wrong tool for it because it changes what the product uses.
    /// </remarks>
    ValueTask<LicenseRegistrationResult> TestLicenseServerAsync( string licenseServerUrl, CancellationToken cancellationToken = default );

    /// <inheritdoc cref="ValidateLicenseKeyAsync"/>
    [Obsolete( "Use ValidateLicenseKeyAsync. This overload blocks the calling thread while a license server is contacted." )]
    LicenseRegistrationResult ValidateLicenseKey( string licenseKey );

    /// <summary>
    /// Attempts to parse a license string into a <see cref="LicenseRegistrationProperties"/>, but does not test
    /// whether it can be registered.
    /// </summary>
    ValueTask<LicenseRegistrationResult> ParseLicenseKeyAsync( string licenseKey, CancellationToken cancellationToken = default );

    /// <inheritdoc cref="ParseLicenseKeyAsync"/>
    [Obsolete( "Use ParseLicenseKeyAsync. This overload blocks the calling thread while a license server is contacted." )]
    LicenseRegistrationResult ParseLicenseKey( string licenseKey );
}
