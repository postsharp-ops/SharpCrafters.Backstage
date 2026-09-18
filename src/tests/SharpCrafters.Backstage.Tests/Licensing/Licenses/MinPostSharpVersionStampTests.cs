// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Licensing;
using SharpCrafters.Backstage.Licensing.Licenses;
using SharpCrafters.Backstage.Testing;
using SharpCrafters.Backstage.Utilities;
using System;
using System.Linq;
using System.Text;
using Xunit;
using Xunit.Abstractions;

namespace SharpCrafters.Backstage.Tests.Licensing.Licenses;

/// <summary>
/// Tests the version that a PostSharp license key declares as the earliest that can read it.
/// </summary>
/// <remarks>
/// <para>
/// PostSharp before 6.5.17, 6.8.10 and 6.9.3 refuses a key carrying any field it does not know, and checks the
/// declared version before it looks at the fields. Declaring 6.9.3 on a key that carries such a field is what makes
/// those versions refuse it with a message naming the version to upgrade to, instead of calling it invalid. The
/// declaration is therefore stamped on the key as it is serialized rather than being left to the caller, because a
/// caller who forgets it produces a key that misreports itself to every old reader.
/// </para>
/// <para>
/// This is the field, and the direction, that <see cref="MinPostSharpVersionTests"/> contrasts itself with: here a
/// key tells an old reader what it needs, there we work out from a key what can read it.
/// </para>
/// </remarks>
#pragma warning disable CS0618 // Type or member is obsolete: the rules must name the products no longer issued.

public sealed class MinPostSharpVersionStampTests : TestsBase
{
    /// <summary>
    /// The version that a key carrying a field of the tolerant format declares, which is the first version on the
    /// main line whose reader steps over a field it does not know.
    /// </summary>
    private static readonly Version _tolerantVersion = new( 6, 9, 3 );

    private static readonly TestLicenseKeyProvider _licenseKeyProvider = new();

    public MinPostSharpVersionStampTests( ITestOutputHelper logger ) : base( logger ) { }

    private static LicenseKeyDataBuilder CreateBuilder( LicenseProduct product )
        => new()
        {
            LicenseId = 910,
            Product = product,
            LicenseType = LicenseType.Business,
            SubscriptionEndDate = _licenseKeyProvider.DefaultSubscriptionExpirationDate
        };

    private static LicenseKeyData Serialize( LicenseKeyDataBuilder builder )
    {
        var licenseKey = builder.SignAndSerialize( TestLicensingAuthorityProvider.DsaTestAuthority );

        Assert.True( LicenseKeyData.TryDeserialize( licenseKey, out var licenseKeyData, out var errorMessage ), errorMessage );

        return licenseKeyData;
    }

    /// <summary>
    /// Every PostSharp product gets the declaration, because a key of any of them may be read by a version of
    /// PostSharp old enough to need it.
    /// </summary>
    [Theory]
    [InlineData( LicenseProduct.PostSharpUltimate )]
    [InlineData( LicenseProduct.PostSharpFramework )]
    [InlineData( LicenseProduct.PostSharpDiagnosticsLibrary )]
    [InlineData( LicenseProduct.PostSharpModelLibrary )]
    [InlineData( LicenseProduct.PostSharpThreadingLibrary )]
    [InlineData( LicenseProduct.PostSharpCachingLibrary )]
    [InlineData( LicenseProduct.PostSharp20 )]
    [InlineData( LicenseProduct.PostSharpUltimate1 )]
    public void APostSharpKeyCarryingANewFieldDeclaresTheTolerantVersion( LicenseProduct product )
    {
        var builder = CreateBuilder( product );

        // The generation is a field of the tolerant format, so a key of the current generation is one that an old
        // reader cannot read.
        builder.Generation = LicenseGeneration.Current;

        Assert.Null( builder.MinPostSharpVersion );
        Assert.Equal( _tolerantVersion, Serialize( builder ).MinPostSharpVersion );
    }

    /// <summary>
    /// A PostSharp key carrying no such field declares nothing, because every released version can read it and a
    /// declaration would only keep it from the versions that can.
    /// </summary>
    [Fact]
    public void APostSharpKeyOfTheOldFormatDeclaresNothing()
    {
        var builder = CreateBuilder( LicenseProduct.PostSharpUltimate );
        builder.SubscriptionEndDate = null;

        Assert.Null( Serialize( builder ).MinPostSharpVersion );
    }

    /// <summary>
    /// A key of the other family declares nothing whatever it carries: no version of PostSharp reads a Metalama key,
    /// so there is no old reader to warn.
    /// </summary>
    [Theory]
    [InlineData( LicenseProduct.MetalamaProfessional )]
    [InlineData( LicenseProduct.MetalamaEnterprise )]
    [InlineData( LicenseProduct.MetalamaCommunity )]
    public void AMetalamaKeyDeclaresNothing( LicenseProduct product )
    {
        var builder = CreateBuilder( product );
        builder.Generation = LicenseGeneration.Current;

        Assert.Null( Serialize( builder ).MinPostSharpVersion );
    }

    /// <summary>
    /// The declaration is not something a caller chooses. There is no way to set it on a builder, so a key we issue
    /// always declares the version the rules give it and a caller cannot get it wrong.
    /// </summary>
    [Fact]
    public void TheDeclarationCannotBeStatedByACaller()
        => Assert.Null( typeof(LicenseKeyDataBuilder).GetProperty( nameof(LicenseKeyDataBuilder.MinPostSharpVersion) )?.SetMethod );

    /// <summary>
    /// A key that already declares another version, and carries a field of the tolerant format, is refused when it is
    /// written again rather than being restamped. Its two statements about itself contradict each other, and choosing
    /// either one would be choosing which of them to disbelieve.
    /// </summary>
    /// <remarks>
    /// Such a key cannot be produced here, because the declaration is stamped and cannot be overridden. It can arrive
    /// from the license key generator, which writes the field itself, so the key under test is built by changing the
    /// declaration of a serialized key to another version of the same length.
    /// </remarks>
    [Fact]
    public void AKeyDeclaringAnotherVersionIsRefusedWhenWrittenAgain()
    {
        var builder = CreateBuilder( LicenseProduct.PostSharpUltimate );
        builder.Generation = LicenseGeneration.Current;

        var licenseKey = builder.Serialize();

        Assert.True( LicenseKeyData.TryDeserialize( licenseKey, out var stamped, out var errorMessage ), errorMessage );
        Assert.Equal( _tolerantVersion, stamped.MinPostSharpVersion );

        // "7.0.0" is as long as "6.9.3", so replacing one with the other leaves every length in the key correct and
        // changes only what the key says about itself.
        var declaringAnotherVersion = ReplaceDeclaration( licenseKey, "6.9.3", "7.0.0" );

        Assert.True( LicenseKeyData.TryDeserialize( declaringAnotherVersion, out var deformed, out var deformedError ), deformedError );
        Assert.Equal( new Version( 7, 0, 0 ), deformed.MinPostSharpVersion );

        Assert.Throws<InvalidOperationException>( () => deformed.ToBuilder().Serialize() );
    }

    /// <summary>
    /// Replaces the declared version in the body of a license key with another of the same length.
    /// </summary>
    private static string ReplaceDeclaration( string licenseKey, string declaration, string replacement )
    {
        Assert.Equal( declaration.Length, replacement.Length );

        var firstDash = licenseKey.IndexOfOrdinal( '-' );
        var body = Base32.FromBase32String( licenseKey.Substring( firstDash + 1 ) );

        var declared = Encoding.ASCII.GetBytes( declaration );
        var position = IndexOf( body, declared );
        Assert.True( position >= 0, $"The key does not declare {declaration}." );

        var deformed = body.ToArray();
        Encoding.ASCII.GetBytes( replacement ).CopyTo( deformed, position );

        return licenseKey.Substring( 0, firstDash + 1 ) + Base32.ToBase32String( deformed, 0 );
    }

    private static int IndexOf( byte[] haystack, byte[] needle )
    {
        for ( var i = 0; i <= haystack.Length - needle.Length; i++ )
        {
            if ( needle.Select( ( b, j ) => haystack[i + j] == b ).All( found => found ) )
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// Serializing a key that was read back gives the same key. The declaration is stamped on the way out, so a key
    /// that goes through the reader and the writer once more would otherwise be stamped a second time and either
    /// change or be refused.
    /// </summary>
    [Fact]
    public void TheDeclarationSurvivesBeingReadAndWrittenAgain()
    {
        var builder = CreateBuilder( LicenseProduct.PostSharpUltimate );
        builder.Generation = LicenseGeneration.Current;

        var licenseKey = builder.SignAndSerialize( TestLicensingAuthorityProvider.DsaTestAuthority );

        Assert.True( LicenseKeyData.TryDeserialize( licenseKey, out var licenseKeyData, out var errorMessage ), errorMessage );
        Assert.Equal( licenseKey, licenseKeyData.ToBuilder().Serialize() );
        Assert.Equal( _tolerantVersion, licenseKeyData.MinPostSharpVersion );
    }

    /// <summary>
    /// The declaration a key carries and the version we work out from its content agree for an ordinary key of the
    /// current generation. They are answers to different questions and are allowed to differ, but a difference here
    /// would mean that a key we issue names one version to the old readers and another to ourselves.
    /// </summary>
    [Fact]
    public void TheDeclarationAgreesWithTheVersionWorkedOutFromTheContent()
    {
        var builder = CreateBuilder( LicenseProduct.PostSharpUltimate );
        builder.Generation = LicenseGeneration.Current;

        var licenseKeyData = Serialize( builder );

        Assert.Equal( licenseKeyData.MinPostSharpVersion, licenseKeyData.GetMinPostSharpVersion() );
    }
}
#pragma warning restore CS0618
