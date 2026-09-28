// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Licensing;
using SharpCrafters.Backstage.Licensing.Consumption;
using SharpCrafters.Backstage.Licensing.Consumption.Sources;
using SharpCrafters.Backstage.Licensing.Licenses;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace SharpCrafters.Backstage.Tests.Licensing.Authority;

/// <summary>
/// Tests that a license key whose signature algorithm the current platform does not implement is reported as invalid,
/// instead of raising the <see cref="PlatformNotSupportedException"/> that the creation of the cryptographic object
/// throws. Finite field DSA is unavailable on macOS since .NET 11, so that is what happens there with every license
/// key issued until 2026.
/// </summary>
public sealed class UnsupportedAlgorithmTests : LicensingTestsBase
{
    private const int _licenseId = 800;

    // The key is signed by the test authority of Elliptic Curve DSA, which the provider of these tests cannot instantiate.
    private static readonly string _expectedErrorMessage =
        $"the license key is signed by the licensing authority {TestLicensingAuthorityProvider.ECDsaTestKeyId}, whose cryptographic algorithm this platform does not support";

    public UnsupportedAlgorithmTests( ITestOutputHelper logger ) : base( logger ) { }

    protected override ILicensingAuthorityProvider CreateLicensingAuthorityProvider( IServiceProvider serviceProvider )
        => new UnsupportedAlgorithmLicensingAuthorityProvider( serviceProvider );

    /// <summary>
    /// Creates a license key that requires a signature, and signs it with an authority whose algorithm is available,
    /// which is what happened to a license key that was issued before its platform dropped the algorithm.
    /// </summary>
    /// <returns>The license key.</returns>
    private static string CreateSignedLicenseKey()
    {
        var builder = new LicenseKeyDataBuilder
        {
            LicenseId = _licenseId,
            Product = LicenseProduct.MetalamaProfessional,
            LicenseType = LicenseType.Business,
            Generation = LicenseGeneration.Current,
            SubscriptionEndDate = LicenseKeyProvider.DefaultSubscriptionExpirationDate
        };

        Assert.True( builder.RequiresSignature() );

        return builder.SignAndSerialize( TestLicensingAuthorityProvider.ECDsaTestAuthority );
    }

    [Fact]
    public void VerificationReportsTheUnsupportedAlgorithm()
    {
        Assert.True( LicenseKeyData.TryDeserialize( CreateSignedLicenseKey(), out var licenseKeyData, out _ ), "Cannot parse." );

        Assert.False( licenseKeyData.TryVerifySignature( this.LicensingAuthorityProvider, out var errorMessage ) );
        Assert.Equal( _expectedErrorMessage, errorMessage );
    }

    [Fact]
    public async Task ConsumptionReportsTheUnsupportedAlgorithm()
    {
        var license = new License( CreateSignedLicenseKey(), this.ServiceProvider );

        var consumptionResult = await license.GetConsumptionPropertiesAsync( LicenseConsumptionOptions.Default );
        Assert.False( consumptionResult.IsSuccess );
        var errorMessage = consumptionResult.ErrorMessage;
        Assert.Equal( _expectedErrorMessage, errorMessage );
    }

    [Fact]
    public async Task RegistrationReportsTheUnsupportedAlgorithm()
    {
        var license = new License( CreateSignedLicenseKey(), this.ServiceProvider );

        var registrationResult = await license.GetRegistrationPropertiesAsync();
        Assert.False( registrationResult.IsSuccess );
        var errorMessage = registrationResult.ErrorMessage;
        Assert.Contains( _expectedErrorMessage, errorMessage, StringComparison.Ordinal );
    }

    /// <summary>
    /// Tests that the message reported for such a key names it by its identifier. The registration properties, which
    /// normally give the name, cannot be read from a key whose signature cannot be verified.
    /// </summary>
    [Fact]
    public async Task ConsumptionServiceNamesTheKey()
    {
        var messages = new List<LicensingMessage>();

        var licenseConsumptionService = new LicenseConsumptionService(
            this.ServiceProvider,
            [new ExplicitLicenseSource( CreateSignedLicenseKey(), LicenseSourceKind.Test, this.ServiceProvider )] );

        await licenseConsumptionService.CreateConsumerAsync( LicenseConsumptionOptions.Default, messages.Add );

        var message = Assert.Single( messages ).Text;

        Assert.StartsWith( $"Cannot use the license '{_licenseId}': {_expectedErrorMessage}", message, StringComparison.Ordinal );
    }

    /// <summary>
    /// A provider whose keys cannot be instantiated, as the finite field DSA keys cannot be on macOS since .NET 11.
    /// </summary>
    private sealed class UnsupportedAlgorithmLicensingAuthorityProvider : LicensingAuthorityProvider
    {
        public UnsupportedAlgorithmLicensingAuthorityProvider( IServiceProvider serviceProvider ) : base(
            serviceProvider,
            [TestLicensingAuthorityProvider.DsaTestKeyId, TestLicensingAuthorityProvider.ECDsaTestKeyId] ) { }

        protected override LicensingAuthority CreateAuthority( byte keyId )
            => throw new PlatformNotSupportedException( $"The algorithm of the key {keyId} is not supported on this platform." );
    }
}
