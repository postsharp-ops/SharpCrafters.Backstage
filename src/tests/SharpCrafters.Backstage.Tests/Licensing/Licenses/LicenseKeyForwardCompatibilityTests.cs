// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Licensing;
using SharpCrafters.Backstage.Licensing.Consumption;
using SharpCrafters.Backstage.Licensing.Licenses;
using System;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace SharpCrafters.Backstage.Tests.Licensing.Licenses;

/// <summary>
/// Tests what this version does with a license key issued by a later one, all the way through consumption.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="LicenseKeyFieldTests"/> shows that such a key reads and writes unchanged. This shows what happens to it
/// afterwards, which is the part the user sees: a key that grants nothing must say so, and a build must not fall over
/// because of a license key that is perfectly valid and merely newer than the product reading it.
/// </para>
/// <para>
/// These cases are reached however careful the registration is, because a license key does not have to be registered
/// to be consumed: it can be given in an environment variable, written into a project file, or leased from a license
/// server, and each of those hands it straight to this path.
/// </para>
/// </remarks>
public sealed class LicenseKeyForwardCompatibilityTests : LicensingTestsBase
{
    public LicenseKeyForwardCompatibilityTests( ITestOutputHelper logger ) : base( logger ) { }

    private static LicenseKeyDataBuilder CreateBuilder()
        => new()
        {
            LicenseId = 930,
            Product = LicenseProduct.MetalamaProfessional,
            LicenseType = LicenseType.Business,
            Generation = LicenseGeneration.Current,
            SubscriptionEndDate = LicenseKeyProvider.DefaultSubscriptionExpirationDate
        };

    private ValueTask<LicenseConsumptionResult> ConsumeAsync( string licenseKey )
        => new License( licenseKey, this.ServiceProvider ).GetConsumptionPropertiesAsync( LicenseConsumptionOptions.Default );

    /// <summary>
    /// A key carrying a field that a later version added, and that this one is not required to understand, grants
    /// what the rest of the key says. This is what the length prefix bought: before it, adding any field to a key
    /// made every released version refuse it.
    /// </summary>
    [Fact]
    public async Task AKeyCarryingAFieldOfALaterVersionIsStillConsumed()
    {
        var builder = CreateBuilder();
        builder.UnknownOptionalField = "a field of a later version";

        var result = await this.ConsumeAsync( builder.SignAndSerialize( TestLicensingAuthorityProvider.DsaTestAuthority ) );

        Assert.True( result.IsSuccess, result.ErrorMessage );
    }

    /// <summary>
    /// A key carrying a field that a later version added and requires is refused, with a message and without an
    /// exception. The field could change what the key grants, so granting the rest of it would be granting something
    /// we cannot read the terms of.
    /// </summary>
    [Fact]
    public async Task AKeyRequiringAFieldOfALaterVersionIsRefusedWithAMessage()
    {
        var builder = CreateBuilder();
        builder.UnknownMustUnderstandField = "a field of a later version";

        var result = await this.ConsumeAsync( builder.SignAndSerialize( TestLicensingAuthorityProvider.DsaTestAuthority ) );

        Assert.False( result.IsSuccess );
        Assert.Contains( "unknown must-understand fields", result.ErrorMessage!, StringComparison.Ordinal );
    }

    /// <summary>
    /// A key signed by an authority that this version does not have is refused with a message, and without an
    /// exception.
    /// </summary>
    /// <remarks>
    /// This is what every version released today will meet the first time we sign with a new key. Before the check
    /// that this test covers, the provider raised <see cref="System.Collections.Generic.KeyNotFoundException"/> and
    /// nothing on the way out caught it, so the whole build fell over rather than reporting a license it could not
    /// verify.
    /// </remarks>
    [Fact]
    public async Task AKeyOfAnAuthorityOfALaterVersionIsRefusedWithAMessage()
    {
        // The identifier of a key that neither the test provider nor the production one has. It stands for the next
        // signing key, whose identifier follows the ones in use today.
        const byte unknownKeyId = 3;

        Assert.DoesNotContain( unknownKeyId, ProductionLicensingAuthorityProvider.KeyIdentifiers );

        var builder = CreateBuilder();

        // Signed first, so that the key carries a signature of the right shape, and then made to name an authority
        // that does not exist. What is under test is the naming, not the signature: a version that has the authority
        // would call this signature invalid, and one that does not cannot get that far.
        builder.SignAndSerialize( TestLicensingAuthorityProvider.DsaTestAuthority );
        builder.SignatureKeyId = unknownKeyId;

        var result = await this.ConsumeAsync( builder.SerializeToLicenseString() );

        Assert.False( result.IsSuccess );
        Assert.Contains( "licensing authority that this version does not know", result.ErrorMessage!, StringComparison.Ordinal );
    }

    /// <summary>
    /// A key naming a product that a later version introduced is refused with a message. The product decides what the
    /// key grants, so a version that cannot name it cannot honour it.
    /// </summary>
    [Fact]
    public async Task AKeyOfAProductOfALaterVersionIsRefusedWithAMessage()
    {
        var builder = CreateBuilder();
        builder.Product = (LicenseProduct) 250;

        var result = await this.ConsumeAsync( builder.SignAndSerialize( TestLicensingAuthorityProvider.DsaTestAuthority ) );

        Assert.False( result.IsSuccess );
        Assert.Contains( "licensed product is unknown", result.ErrorMessage!, StringComparison.Ordinal );
    }

    /// <summary>
    /// None of these keys brings anything down: each of them is answered, and answered with something that names
    /// what is wrong.
    /// </summary>
    [Fact]
    public async Task NoKeyOfALaterVersionThrows()
    {
        var deformations = new Action<LicenseKeyDataBuilder>[]
        {
            b => b.UnknownOptionalField = "later",
            b => b.UnknownMustUnderstandField = "later",
            b => b.Product = (LicenseProduct) 250,
            b => b.LicenseType = (LicenseType) 250
        };

        foreach ( var deformation in deformations )
        {
            var builder = CreateBuilder();
            deformation( builder );

            var result = await this.ConsumeAsync( builder.SignAndSerialize( TestLicensingAuthorityProvider.DsaTestAuthority ) );

            Assert.True( result.IsSuccess || !string.IsNullOrWhiteSpace( result.ErrorMessage ) );
        }
    }
}
