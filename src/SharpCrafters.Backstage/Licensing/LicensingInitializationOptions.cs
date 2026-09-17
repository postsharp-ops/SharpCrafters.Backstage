// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using JetBrains.Annotations;
using SharpCrafters.Backstage.Licensing.Consumption.Sources;
using SharpCrafters.Backstage.Licensing.Licenses;
using System;

namespace SharpCrafters.Backstage.Licensing;

[PublicAPI]
public record LicensingInitializationOptions
{
    public static LicensingInitializationOptions Default { get; } = new();

    /// <summary>
    /// Gets the catalog of the products whose license keys the product family consumes. The host that composes the
    /// packages must set it; the Metalama umbrella sets the Metalama catalog when it is <c>null</c>.
    /// </summary>
    public ILicenseProductCatalog? ProductCatalog { get; init; }

    /// <summary>
    /// Gets a delegate that creates the provider of the licensing authorities, that is, of the public keys that
    /// verify the signature of a license key. The default creates <see cref="ProductionLicensingAuthorityProvider"/>,
    /// which holds the production keys of PostSharp Technologies. The delegate is ignored when
    /// <see cref="UseTestAuthority"/> is <c>true</c>.
    /// </summary>
    public Func<IServiceProvider, ILicensingAuthorityProvider> AuthorityProviderFactory { get; init; } =
        serviceProvider => new ProductionLicensingAuthorityProvider( serviceProvider );

    public static LicensingInitializationOptions ForTest( Action<LicenseKeyDataBuilder> buildTestLicenseAction )
        => new() { UseTestAuthority = true, IgnoredLicenseSources = LicenseSourceKind.All, BuildTestLicenseAction = buildTestLicenseAction };

    /// <summary>
    /// Gets the time after which a request to a license server is abandoned. The default is ten seconds.
    /// </summary>
    /// <remarks>
    /// A lease is acquired while a compilation waits for it, so the timeout has to be short. PostSharp used the
    /// hundred-second default of <c>WebClient</c>, which stalls a build for far too long when a server is down.
    /// </remarks>
    public TimeSpan LicenseServerTimeout { get; init; } = TimeSpan.FromSeconds( 10 );

    /// <summary>
    /// Gets a value indicating whether a request to a license server authenticates with the credentials of the user
    /// who runs the current process, that is, with Windows integrated authentication. The default is <c>true</c>.
    /// </summary>
    /// <remarks>
    /// An on-premises license server is typically published by IIS with Windows authentication and answers 401 to an
    /// anonymous request. PostSharp sent the default network credentials for that reason.
    /// </remarks>
    public bool LicenseServerUsesDefaultCredentials { get; init; } = true;

    internal LicenseSourceKind IgnoredLicenseSources { get; init; } = LicenseSourceKind.None;

    /// <summary>
    /// Gets a value indicating whether the test licensing authority should be used.
    /// This property may be set to <c>true</c> only for unit tests.
    /// </summary>
    internal bool UseTestAuthority { get; init; }

    /// <summary>
    /// Gets a delegate that can configure a <see cref="LicenseKeyDataBuilder"/>.
    /// This property is only taken into account when <see cref="UseTestAuthority"/> is <c>true</c>.
    /// In this case, profile licenses are not loaded.
    /// </summary>
    internal Action<LicenseKeyDataBuilder>? BuildTestLicenseAction { get; init; }
}