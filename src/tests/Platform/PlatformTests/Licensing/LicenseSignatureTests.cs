// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Extensibility;
using SharpCrafters.Backstage.Licensing.Licenses;
using SharpCrafters.Backstage.Testing;
using Xunit;

namespace SharpCrafters.Backstage.PlatformTests.Licensing;

/// <summary>
/// Signs and verifies a license key with the finite field DSA algorithm and SHA-1, which the cryptography library of the
/// operating system implements: OpenSSL on Linux, and the Apple library on macOS. Either can refuse the algorithm, for
/// instance through the cryptography policy of a Linux distribution. Windows runs the same code in the unit tests.
/// </summary>
public sealed class LicenseSignatureTests
{
    private readonly ILicensingAuthorityProvider _authorities =
        PlatformTestServices.CreateServiceProvider( addLicensing: true ).GetRequiredBackstageService<ILicensingAuthorityProvider>();

    private readonly string _licenseKey = new TestLicenseKeyProvider().MetalamaProfessionalBusiness;

    [PlatformFact( TestPlatforms.Unix )]
    public void ADsaSignatureIsVerified()
    {
        Assert.True( LicenseKeyData.TryDeserialize( this._licenseKey, out var data, out var errorMessage ), errorMessage );
        Assert.True( data.TryVerifySignature( this._authorities, out errorMessage ), errorMessage );
    }

    [PlatformFact( TestPlatforms.Unix )]
    public void ATamperedDsaSignatureIsRejected()
    {
        Assert.True( LicenseKeyDataBuilder.TryDeserialize( this._licenseKey, out var builder, out var errorMessage ), errorMessage );
        builder.LicenseId++;

        Assert.True( LicenseKeyData.TryDeserialize( builder.Serialize(), out var data, out errorMessage ), errorMessage );
        Assert.False( data.TryVerifySignature( this._authorities, out _ ) );
    }
}
