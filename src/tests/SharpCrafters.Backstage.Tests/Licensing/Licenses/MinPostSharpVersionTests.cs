// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Licensing;
using SharpCrafters.Backstage.Licensing.Licenses;
using SharpCrafters.Backstage.Testing;
using System;
using Xunit;
using Xunit.Abstractions;

namespace SharpCrafters.Backstage.Tests.Licensing.Licenses;

/// <summary>
/// Tests the minimal version of PostSharp that can consume a license key.
/// </summary>
/// <remarks>
/// <para>
/// The question is which of the versions already released accept a key, which is what decides where the key is stored
/// so that the versions choking on it never read it. It is answered from the content of the key: a reader released
/// before a field existed rejects a key carrying that field, and a reader that does not know the Elliptic Curve DSA
/// licensing authority of #1864 reports the signature as invalid whatever the rest of the key says.
/// </para>
/// <para>
/// This is not the <see cref="LicenseKeyData.MinPostSharpVersion"/> field, which looks the other way: it lets a key
/// declare the version to upgrade to, for a message that names it.
/// </para>
/// </remarks>
#pragma warning disable CS0618 // Type or member is obsolete: the rules must name the products no longer issued.

public sealed class MinPostSharpVersionTests : TestsBase
{
    private static readonly Version _firstVersionSupportingECDsa = new( 2027, 0 );

    /// <summary>
    /// The first reader that skips a field it does not know instead of rejecting the key. Every key of the current
    /// generation carries the generation and the servicing phase, which are length-prefixed fields, so this is what
    /// such a key needs before its signature is taken into account.
    /// </summary>
    private static readonly Version _firstTolerantVersion = new( 6, 9, 3 );

    private static readonly TestLicenseKeyProvider _licenseKeyProvider = new();

    public MinPostSharpVersionTests( ITestOutputHelper logger ) : base( logger ) { }

    private static LicenseKeyDataBuilder CreateBuilder( LicenseProduct product = LicenseProduct.PostSharpUltimate )
        => new()
        {
            LicenseId = 802,
            Product = product,
            LicenseType = LicenseType.Business,
            Generation = LicenseGeneration.Current,
            SubscriptionEndDate = _licenseKeyProvider.DefaultSubscriptionExpirationDate
        };

    private static LicenseKeyData Deserialize( string licenseKey )
    {
        Assert.True( LicenseKeyData.TryDeserialize( licenseKey, out var licenseKeyData, out var errorMessage ), errorMessage );

        return licenseKeyData;
    }

    /// <summary>
    /// A key signed by the finite field DSA authority needs only the version that its content asks for, because every
    /// version verifies that signature. This is the case that must not change.
    /// </summary>
    [Theory]
    [InlineData( LicenseProduct.PostSharpUltimate )]
    [InlineData( LicenseProduct.PostSharpFramework )]
    [InlineData( LicenseProduct.PostSharpCachingLibrary )]
    public void ADsaSignedKeyNeedsOnlyWhatItsContentAsksFor( LicenseProduct product )
    {
        var licenseKeyData = Deserialize( CreateBuilder( product ).SignAndSerialize( _licenseKeyProvider.Authority ) );

        Assert.False( licenseKeyData.IsSignedByECDsaKey() );
        Assert.Equal( _firstTolerantVersion, licenseKeyData.GetMinPostSharpVersion() );
    }

    /// <summary>
    /// A key signed by the Elliptic Curve DSA authority requires the first version that verifies that signature,
    /// which is later than the version its content asks for.
    /// </summary>
    [Theory]
    [InlineData( LicenseProduct.PostSharpUltimate )]
    [InlineData( LicenseProduct.PostSharpFramework )]
    [InlineData( LicenseProduct.PostSharpCachingLibrary )]
    public void AnECDsaSignedKeyRequiresTheVersionThatVerifiesIt( LicenseProduct product )
    {
        var licenseKeyData = Deserialize( CreateBuilder( product ).SignAndSerialize( TestLicensingAuthorityProvider.ECDsaTestAuthority ) );

        Assert.True( licenseKeyData.IsSignedByECDsaKey() );
        Assert.Equal( _firstVersionSupportingECDsa, licenseKeyData.GetMinPostSharpVersion() );
    }

    /// <summary>
    /// The authority that signs the key is the only difference between the two results, so the requirement comes from
    /// the signature and not from anything else in the key, and it raises the requirement rather than lowering it.
    /// </summary>
    [Fact]
    public void OnlyTheSigningAuthorityDecidesBetweenTheTwo()
    {
        var dsaVersion = Deserialize( CreateBuilder().SignAndSerialize( _licenseKeyProvider.Authority ) ).GetMinPostSharpVersion();

        var ecdsaVersion = Deserialize( CreateBuilder().SignAndSerialize( TestLicensingAuthorityProvider.ECDsaTestAuthority ) )
            .GetMinPostSharpVersion();

        Assert.Equal( _firstTolerantVersion, dsaVersion );
        Assert.Equal( _firstVersionSupportingECDsa, ecdsaVersion );
        Assert.True( ecdsaVersion > dsaVersion, "The signature must raise the requirement, never lower it." );
    }

    /// <summary>
    /// The two families decide the requirement of the signature on the same ground and name the same version.
    /// </summary>
    [Fact]
    public void TheTwoFamiliesAgreeOnTheVersionThatVerifiesTheSignature()
    {
        var licenseKeyData = Deserialize( CreateBuilder().SignAndSerialize( TestLicensingAuthorityProvider.ECDsaTestAuthority ) );

        Assert.Equal( licenseKeyData.GetMinMetalamaVersion(), licenseKeyData.GetMinPostSharpVersion() );
    }

    /// <summary>
    /// A key that carries a length-prefixed field needs the first reader that skips a field it does not know. The
    /// generation and the servicing phase are the fields the generator writes there, and a key of the current
    /// generation carries them.
    /// </summary>
    [Fact]
    public void ALengthPrefixedFieldNeedsTheTolerantReader()
    {
        var licenseKeyData = Deserialize( CreateBuilder().SignAndSerialize( _licenseKeyProvider.Authority ) );

        Assert.True( licenseKeyData.HasLengthPrefixedField );
        Assert.Equal( _firstTolerantVersion, licenseKeyData.GetMinPostSharpVersion() );
    }

    /// <summary>
    /// A key of the products introduced by PostSharp 6.6 needs 6.6, even when it carries nothing that a later reader
    /// added.
    /// </summary>
    /// <remarks>
    /// The previous logic answered 3.0 for a key of Ultimate or of Framework, because it named the caching library
    /// alone among the products of 6.6 and fell back to the oldest version for the other two. PostSharp 3.0 does not
    /// know those product bytes and rejects such a key.
    /// </remarks>
    [Theory]
    [InlineData( LicenseProduct.PostSharpUltimate )]
    [InlineData( LicenseProduct.PostSharpFramework )]
    [InlineData( LicenseProduct.PostSharpCachingLibrary )]
    public void AProductIntroducedBy66NeedsIt( LicenseProduct product )
    {
        var builder = CreateBuilder( product );
        builder.Generation = LicenseGeneration.None;
        builder.ServicingPhase = ServicingPhase.Current;
        builder.SubscriptionEndDate = null;

        var licenseKeyData = Deserialize( builder.SignAndSerialize( _licenseKeyProvider.Authority ) );

        Assert.False( licenseKeyData.HasLengthPrefixedField );
        Assert.Equal( new Version( 6, 6, 0 ), licenseKeyData.GetMinPostSharpVersion() );
    }

    /// <summary>
    /// A key of the legacy encoding of the same products needs only PostSharp 3.0, which is what makes that encoding
    /// worth issuing: it is the one that the readers between 3.0 and 2026.0 all accept.
    /// </summary>
    [Fact]
    public void TheLegacyEncodingNeedsOnly30()
    {
        var builder = CreateBuilder( LicenseProduct.PostSharpUltimate1 );
        builder.Generation = LicenseGeneration.None;
        builder.ServicingPhase = ServicingPhase.Current;
        builder.SubscriptionEndDate = null;

        var licenseKeyData = Deserialize( builder.SignAndSerialize( _licenseKeyProvider.Authority ) );

        Assert.Equal( new Version( 3, 0, 0 ), licenseKeyData.GetMinPostSharpVersion() );
    }

    /// <summary>
    /// The oldest product needs the oldest reader.
    /// </summary>
    [Fact]
    public void ThePostSharp20ProductNeedsOnly20()
    {
        var builder = CreateBuilder( LicenseProduct.PostSharp20 );
        builder.Generation = LicenseGeneration.None;
        builder.ServicingPhase = ServicingPhase.Current;
        builder.SubscriptionEndDate = null;

        var licenseKeyData = Deserialize( builder.SignAndSerialize( _licenseKeyProvider.Authority ) );

        Assert.Equal( new Version( 2, 0, 0 ), licenseKeyData.GetMinPostSharpVersion() );
    }

    /// <summary>
    /// The subscription end date was added by PostSharp 3.0, so a key of the oldest product carrying it needs 3.0
    /// rather than 2.0. This is the rule that dates a key by its fields rather than by its product.
    /// </summary>
    [Fact]
    public void AFieldAddedBy30RaisesTheOldestProductToIt()
    {
        var builder = CreateBuilder( LicenseProduct.PostSharp20 );
        builder.Generation = LicenseGeneration.None;
        builder.ServicingPhase = ServicingPhase.Current;

        var licenseKeyData = Deserialize( builder.SignAndSerialize( _licenseKeyProvider.Authority ) );

        Assert.Equal( new Version( 3, 0, 0 ), licenseKeyData.GetMinPostSharpVersion() );
    }
}
#pragma warning restore CS0618
