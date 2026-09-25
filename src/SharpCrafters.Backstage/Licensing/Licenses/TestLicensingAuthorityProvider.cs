// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using System;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace SharpCrafters.Backstage.Licensing.Licenses;

// Allow weak cryptography
#pragma warning disable CA5384

/// <summary>
/// Provides the authorities of the keys that sign a test license key and verify its signature.
/// </summary>
/// <remarks>
/// <para>
/// There is one key per signature algorithm. The key 255 is a finite field DSA key and the key 254 an Elliptic Curve
/// DSA key. The identifiers 0, 1 and 2, which the production provider owns, are excluded from this provider.
/// </para>
/// <para>
/// The Elliptic Curve DSA key is generated in the current process, so a license key that it signs is valid in the
/// current process only. The finite field DSA key is a fixed key, because macOS can import a finite field DSA key but
/// cannot generate one. A license key that it signs is therefore valid in any process that uses the test authority,
/// and in no process that uses the production authority.
/// </para>
/// </remarks>
internal sealed class TestLicensingAuthorityProvider : LicensingAuthorityProvider
{
    /// <summary>
    /// The identifier of the finite field DSA key of the test provider.
    /// </summary>
    public const byte DsaTestKeyId = 255;

    /// <summary>
    /// The identifier of the Elliptic Curve DSA key of the test provider.
    /// </summary>
    public const byte ECDsaTestKeyId = 254;

    /// <summary>
    /// The finite field DSA key of the test provider, with its private part. It is a test key only. It was generated for
    /// this provider, it signs nothing but test license keys, and no production authority trusts it: the production
    /// provider has no key of identifier <see cref="DsaTestKeyId"/>.
    /// </summary>
    /// <remarks>
    /// The key is fixed rather than generated with <see cref="DSA.Create()"/>, because macOS imports a finite field DSA key
    /// but throws <see cref="PlatformNotSupportedException"/> when asked to generate one. It is 1024 bits long, like the
    /// production keys that <see cref="DsaLicensingAuthority"/> verifies.
    /// </remarks>
    private const string _dsaTestKey =
        "<DSAKeyValue>"
        + "<P>kMIcFGnzkHvtxeiSGhlZFgIcRxry8BiF3GZSPrfzvkk5abttcTZ0XqA+GSP/A7PzLMWF9kCxegxfMd0R8V4WyXtRksrNG5tU0gzw29Gxru4hC/TpEo/HsAV70GqqwoOuGY4cKyWF2uplDckaDdIc3pT3U3ytc8obmeR8k8ozGQc=</P>"
        + "<Q>81XVsyIPHGqem2ou3QSqWnCrSGU=</Q>"
        + "<G>TBgPH6bCAnbtNlyahFfIH2VOuS9g5XeqYjscttDjPquAjvBQffQAlhp6qJCcxHxWxddN3Zw5O24zPXQLMT6c8Q8E7C4u9NAs3BCregb3AfWAAJd61yzVKGHZySmOGFkmZG5Tx3wMWrZZyxYdNI0lASFIc9AJ5p4WA8m0zFwvZRY=</G>"
        + "<Y>DXwKXT4xLIa0NEf1idsLezvpFX0XNAOIzFEUXLsPY9ZtLPslVOtgJJh+2qrxbGkA3Xf4FSyAiwWhMGjlldsQpPkT3a7hdjLSegYJv8aPGoUQLDAcCxjQKRWU77qB+UwSBV1qtJMDcbdhbo/BrjadqV9YvufchmGVZ4Ez3mkrWi8=</Y>"
        + "<Seed>NZxVJFoFPMLr+H9t1xRK6JVSKA8=</Seed>"
        + "<PgenCounter>gA==</PgenCounter>"
        + "<X>nTbthb2PK8UjO5qNYwSdyrGR1BM=</X>"
        + "</DSAKeyValue>";

    private static readonly Lazy<LicensingAuthority> _dsaTestAuthority = new( () => new DsaLicensingAuthority( DsaTestKeyId, _dsaTestKey ) );

    private static readonly Lazy<LicensingAuthority> _ecdsaTestAuthority =
        new( () => new ECDsaLicensingAuthority( ECDsaTestKeyId, ECDsa.Create( ECCurve.NamedCurves.nistP256 ) ) );

    /// <summary>
    /// Gets the finite field DSA authority of the test provider.
    /// </summary>
    /// <remarks>
    /// Every instance of the test provider returns this authority, and the test license key provider signs with it,
    /// so a license key signed with it is verified by any service provider that uses the test authority.
    /// </remarks>
    public static LicensingAuthority DsaTestAuthority => _dsaTestAuthority.Value;

    /// <summary>
    /// Gets the Elliptic Curve DSA authority of the test provider. It is shared in the same way as
    /// <see cref="DsaTestAuthority"/>.
    /// </summary>
    public static LicensingAuthority ECDsaTestAuthority => _ecdsaTestAuthority.Value;

    /// <summary>
    /// Initializes a new instance of the <see cref="TestLicensingAuthorityProvider"/> class.
    /// </summary>
    /// <param name="serviceProvider">The service provider that provides the observer of the new provider, or <c>null</c> if the new provider has no observer.</param>
    public TestLicensingAuthorityProvider( IServiceProvider? serviceProvider = null ) : base( serviceProvider, [DsaTestKeyId, ECDsaTestKeyId] ) { }

    /// <inheritdoc />
    protected override LicensingAuthority CreateAuthority( byte keyId )
        => keyId switch
        {
            DsaTestKeyId => DsaTestAuthority,
            ECDsaTestKeyId => ECDsaTestAuthority,
            _ => throw new KeyNotFoundException( $"There is no test licensing authority key of identifier {keyId}." )
        };
}
