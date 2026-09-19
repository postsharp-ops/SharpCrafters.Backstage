// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using JetBrains.Annotations;
using SharpCrafters.Backstage.Extensibility;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;

namespace SharpCrafters.Backstage.Licensing.Registration;

[PublicAPI]
public interface ILicenseRegistrationService : IBackstageService, INotifyPropertyChanged
{
    /// <summary>
    /// Registers one of the editions that the product family gives away.
    /// </summary>
    /// <param name="edition">One of <see cref="ILicenseProductCatalog.SelfRegisteredEditions"/>.</param>
    /// <param name="options">What the user chose, such as why they are entitled to the edition.</param>
    /// <remarks>
    /// There is one method for every edition of every family, rather than one per edition, because the editions are
    /// what a family declares and this interface knows no family. An edition carries its own procedure.
    /// </remarks>
    LicenseRegistrationResult Register( SelfRegisteredEdition edition, SelfRegisteredEditionOptions? options = null );

    /// <summary>
    /// Gets the editions that can be registered on this machine as things stand, in the order in which they are
    /// offered.
    /// </summary>
    /// <remarks>
    /// An edition whose availability depends on the state of the machine is absent while it cannot be registered: the
    /// trial is, for as long as one is running or its cool-off period has not elapsed. This is a property rather than
    /// a method so that a user interface bound to it is told when the licensing configuration changes.
    /// </remarks>
    ImmutableArray<SelfRegisteredEdition> AvailableEditions { get; }

    /// <summary>
    /// Registers a license string, which is either a license key or the URL of a license server.
    /// </summary>
    /// <param name="licenseString">The license key, or the URL of a license server.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <remarks>
    /// Registering the URL of a license server contacts it, and therefore takes a seat, so that the user learns at
    /// once that the URL is wrong or that the server has no licence for them, rather than on their next build.
    /// </remarks>
    ValueTask<LicenseRegistrationResult> RegisterLicenseAsync( string licenseString, CancellationToken cancellationToken = default );

    /// <inheritdoc cref="RegisterLicenseAsync"/>
    [Obsolete( "Use RegisterLicenseAsync. This overload blocks the calling thread while a license server is contacted." )]
    LicenseRegistrationResult RegisterLicense( string licenseString );

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
    /// Acquires a lease from the registered license server and reports the licence it leases.
    /// </summary>
    /// <param name="forceRenewal">
    /// <see langword="true"/> to renew the lease even when the one currently held is valid and not yet due for
    /// renewal. <see langword="false"/>, the default, does what a build does: the stored lease is used when it is
    /// still good, and the server is contacted only when it is not.
    /// </param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <remarks>
    /// <para>
    /// This is what a build does, made explicit: it contacts the server, it takes a seat, and it stores the lease.
    /// Diagnosing an on-premises server is the most common support interaction for this feature, and doing it by
    /// acquiring a lease means that what the user sees is what their next build will see, rather than the result of a
    /// separate code path.
    /// </para>
    /// <para>
    /// The server is the one that is registered, and there is no way to name another: a server that is not registered
    /// is not what the product will use, so reporting on it would answer a question nobody asked. Registering a URL
    /// contacts it too, so <see cref="RegisterLicenseAsync"/> is what diagnoses a server that is not registered yet.
    /// </para>
    /// <para>
    /// This requires an interactive session, because an unattended process never leases: it is licensed by the
    /// unattended license, which costs nothing, and a build server that leased would hold seats that the people who
    /// need them cannot get. A license source skips a registered license server for the same reason.
    /// </para>
    /// </remarks>
    ValueTask<LicenseRegistrationResult> AcquireLeaseAsync( bool forceRenewal = false, CancellationToken cancellationToken = default );

    /// <inheritdoc cref="ValidateLicenseKeyAsync"/>
    [Obsolete( "Use ValidateLicenseKeyAsync. This overload blocks the calling thread while a license server is contacted." )]
    LicenseRegistrationResult ValidateLicenseKey( string licenseKey );

    /// <summary>
    /// Resolves a license string into a <see cref="LicenseRegistrationProperties"/>, without testing whether it can
    /// be registered and without registering it.
    /// </summary>
    /// <param name="licenseString">The license key, or the URL of a license server.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <remarks>
    /// This is not a parse. A license key is deserialized and nothing else happens, but the URL of a license server
    /// is resolved by contacting that server and taking a lease from it, which takes a seat from the pool of the
    /// team. The method is named for what it does in the expensive case rather than in the cheap one.
    /// </remarks>
    ValueTask<LicenseRegistrationResult> ResolveLicenseAsync( string licenseString, CancellationToken cancellationToken = default );

    /// <inheritdoc cref="ResolveLicenseAsync"/>
    [Obsolete( "Use ResolveLicenseAsync. This overload blocks the calling thread while a license server is contacted." )]
    LicenseRegistrationResult ParseLicenseKey( string licenseKey );
}
