// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Licensing;
using SharpCrafters.Backstage.Licensing.Licenses;
using SharpCrafters.Backstage.Licensing.Licenses.LicenseFields;
using SharpCrafters.Backstage.Testing;
using SharpCrafters.Backstage.Utilities;
using System;
using System.Globalization;
using System.Linq;
using Xunit;
using Xunit.Abstractions;

namespace SharpCrafters.Backstage.Tests.Licensing.Licenses;

/// <summary>
/// Tests that a string which is not a license key is refused with a reason rather than accepted or thrown out of.
/// </summary>
/// <remarks>
/// <para>
/// Every one of these strings arrives from outside the product: a user types a key by hand, pastes half of one, or
/// edits the registry. The reader must therefore treat its input as hostile, and in particular must not let an
/// exception out: the callers of <see cref="LicenseKeyData.TryDeserialize"/> all take <see langword="false"/> to
/// mean "this is not a license key" and have nowhere to put an exception.
/// </para>
/// <para>
/// The malformed keys are built by taking a key apart rather than by writing bytes here, so that a change to the
/// format changes what these tests feed the reader instead of leaving them exercising a shape that no longer exists.
/// The key taken apart is unsigned, because a signature is forty or sixty-four bytes of a value that differs at
/// every signing, and a test that looks for a byte at a position would then find it only sometimes.
/// </para>
/// </remarks>
public sealed class LicenseKeyParsingTests : TestsBase
{
    /// <summary>
    /// The identifier of the keys under test. Its four bytes are 0x91, 0x03, 0x00 and 0x00, none of which is the
    /// index of a field or the terminator, so a test looking for one of those bytes does not find this instead.
    /// </summary>
    private const int _licenseId = 913;

    private static readonly TestLicenseKeyProvider _licenseKeyProvider = new();

    public LicenseKeyParsingTests( ITestOutputHelper logger ) : base( logger ) { }

    private static LicenseKeyDataBuilder CreateBuilder()
        => new()
        {
            LicenseId = _licenseId,
            Product = LicenseProduct.MetalamaProfessional,
            LicenseType = LicenseType.Business,
            Generation = LicenseGeneration.Current,
            SubscriptionEndDate = _licenseKeyProvider.DefaultSubscriptionExpirationDate
        };

    /// <summary>
    /// Creates a key that carries no signature, so that its bytes are the same at every run.
    /// </summary>
    private static string CreateUnsignedLicenseKey() => CreateBuilder().Serialize();

    /// <summary>
    /// Takes the body of a license key apart into the bytes it encodes.
    /// </summary>
    private static (string Prefix, byte[] Body) Split( string licenseKey )
    {
        var firstDash = licenseKey.IndexOfOrdinal( '-' );

        return (licenseKey.Substring( 0, firstDash ), Base32.FromBase32String( licenseKey.Substring( firstDash + 1 ) ));
    }

    private static string Join( string prefix, byte[] body ) => prefix + "-" + Base32.ToBase32String( body, 0 );

    /// <summary>
    /// Asserts that a string is refused, and that the refusal carries a reason rather than an empty message.
    /// </summary>
    private static void AssertRefused( string licenseKey, string because )
    {
        Assert.False( LicenseKeyData.TryDeserialize( licenseKey, out _, out var errorMessage ), because );
        Assert.False( string.IsNullOrWhiteSpace( errorMessage ), "The key was refused without a reason." );
    }

    /// <summary>
    /// The key that the other tests deform is itself accepted, and taking it apart and putting it together again
    /// gives it back, so a test that changes one byte of the body has changed exactly that.
    /// </summary>
    [Fact]
    public void TheKeyTheOtherTestsDeformIsAccepted()
    {
        var licenseKey = CreateUnsignedLicenseKey();

        Assert.True( LicenseKeyData.TryDeserialize( licenseKey, out var licenseKeyData, out var errorMessage ), errorMessage );
        Assert.Equal( _licenseId, licenseKeyData.LicenseId );

        var (prefix, body) = Split( licenseKey );
        Assert.Equal( licenseKey, Join( prefix, body ) );

        // Nothing in the body is the terminator except the final byte, which is what lets the truncation test know
        // that a shortened body always runs out rather than ending early.
        Assert.Equal( (byte) LicenseFieldIndex.End, body[body.Length - 1] );
        Assert.DoesNotContain( (byte) LicenseFieldIndex.End, body.Take( body.Length - 1 ) );
    }

    [Theory]
    [InlineData( "" )]
    [InlineData( " " )]
    [InlineData( "no-dash-is-not-enough" )]
    [InlineData( "913" )]
    [InlineData( "913-" )]
    [InlineData( "913-NOTBASE32!!" )]
    [InlineData( "-AAAA" )]
    [InlineData( "this is not a license key at all" )]
    public void SomethingThatIsNotALicenseKeyIsRefused( string licenseKey ) => AssertRefused( licenseKey, "It is not a license key." );

    /// <summary>
    /// A key with no separator at all cannot be told apart into a prefix and a body.
    /// </summary>
    [Fact]
    public void AKeyWithoutASeparatorIsRefused()
    {
        var licenseKey = CreateUnsignedLicenseKey();
        var firstDash = licenseKey.IndexOfOrdinal( '-' );

        AssertRefused(
            licenseKey.Substring( 0, firstDash ) + licenseKey.Substring( firstDash + 1 ),
            "The key has no separator." );
    }

    /// <summary>
    /// The identifier is written twice, in the prefix and in the body, and a key whose two copies disagree has been
    /// edited. Accepting it would let the prefix name one license while the body grants another.
    /// </summary>
    [Fact]
    public void AKeyWhoseIdentifierDisagreesWithItsBodyIsRefused()
    {
        var (_, body) = Split( CreateUnsignedLicenseKey() );

        AssertRefused( Join( (_licenseId + 1).ToString( CultureInfo.InvariantCulture ), body ), "The prefix names another license." );
    }

    /// <summary>
    /// A key with anything after its final field has been appended to. The trailing bytes are not covered by the
    /// signature, so a reader that ignored them would accept a key that carries whatever the appender chose.
    /// </summary>
    [Fact]
    public void AKeyWithTrailingBytesIsRefused()
    {
        var (prefix, body) = Split( CreateUnsignedLicenseKey() );

        AssertRefused( Join( prefix, body.Concat( new byte[] { 0 } ).ToArray() ), "The key has one trailing byte." );
        AssertRefused( Join( prefix, body.Concat( new byte[] { 1, 2, 3, 4 } ).ToArray() ), "The key has four trailing bytes." );
    }

    /// <summary>
    /// A key that stops in the middle of a field cannot be read, and must be refused rather than read as far as it
    /// goes.
    /// </summary>
    [Fact]
    public void ATruncatedKeyIsRefused()
    {
        var (prefix, body) = Split( CreateUnsignedLicenseKey() );

        for ( var length = 0; length < body.Length; length++ )
        {
            AssertRefused( Join( prefix, body.Take( length ).ToArray() ), $"The key stops after {length} of {body.Length} bytes." );
        }
    }

    /// <summary>
    /// A field whose index is below the length-prefixed range and which this version does not know cannot be
    /// stepped over, because nothing says how long it is. Such a key is refused.
    /// </summary>
    /// <remarks>
    /// This is the shape that the length prefix was introduced to retire: before it, every field added made every
    /// released version refuse the key. Index 6 is in that old range and has never been issued.
    /// </remarks>
    [Fact]
    public void AnUnknownFieldThatCarriesNoLengthIsRefused()
    {
        const byte unknownFieldWithoutLength = 6;

        Assert.False( ((LicenseFieldIndex) unknownFieldWithoutLength).IsPrefixedByLength() );

        var (prefix, body) = Split( CreateUnsignedLicenseKey() );

        // Put the field just before the terminator, which is the final byte of the body.
        var deformed = body.Take( body.Length - 1 )
            .Concat( new byte[] { unknownFieldWithoutLength, 0, (byte) LicenseFieldIndex.End } )
            .ToArray();

        AssertRefused( Join( prefix, deformed ), "The key carries a field of unknowable length." );
    }

    /// <summary>
    /// A field of a known, fixed width whose declared length is not that width has been edited, and is refused
    /// rather than read at the length it declares.
    /// </summary>
    [Fact]
    public void AFieldWhoseDeclaredLengthIsWrongIsRefused()
    {
        var (prefix, body) = Split( CreateUnsignedLicenseKey() );

        // The generation is one byte wide and is written as [index][length][value]. The key under test carries it
        // exactly once, so the byte that follows its index is the length to deform.
        var index = (byte) LicenseFieldIndex.Generation;
        Assert.Equal( 1, body.Count( b => b == index ) );

        var position = Array.IndexOf( body, index );
        Assert.Equal( 1, body[position + 1] );

        var deformed = body.ToArray();
        deformed[position + 1] = 2;

        AssertRefused( Join( prefix, deformed ), "A fixed-width field declares the wrong length." );
    }

    /// <summary>
    /// A key whose content has been changed no longer matches its signature, although it still reads. A key has to
    /// be read before its signature can be checked, so the reader accepting it is not the failure; the signature
    /// refusing it is the point.
    /// </summary>
    [Fact]
    public void AKeyCarryingTheSignatureOfAnotherKeyDoesNotVerify()
    {
        var authorityProvider = new TestLicensingAuthorityProvider();

        var signedKey = CreateBuilder().SignAndSerialize( TestLicensingAuthorityProvider.DsaTestAuthority );
        Assert.True( LicenseKeyData.TryDeserialize( signedKey, out var signed, out var parseError ), parseError );
        Assert.True( signed.TryVerifySignature( authorityProvider, out var signatureError ), signatureError );

        // The same license, granted to someone else, carrying the signature that was made for the original. This is
        // what an edited key amounts to, and it is built rather than patched so that the edit is exact.
        var tamperedBuilder = CreateBuilder();
        tamperedBuilder.Licensee = "someone else";
        tamperedBuilder.SignatureKeyId = signed.SignatureKeyId;
        tamperedBuilder.Signature = signed.Signature;

        var tamperedKey = tamperedBuilder.SerializeToLicenseString();

        Assert.True( LicenseKeyData.TryDeserialize( tamperedKey, out var tampered, out var tamperedParseError ), tamperedParseError );
        Assert.Equal( "someone else", tampered.Licensee );
        Assert.False( tampered.TryVerifySignature( authorityProvider, out _ ), "An edited key must not verify." );
    }

    /// <summary>
    /// A key signed by an authority that this version does not have is refused with a reason, rather than throwing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is what every released version will do when we next rotate the signing key. The production provider
    /// holds the keys 0, 1 and 2 today; a key issued under the key 3 names an authority that no released version
    /// has, and reaches <see cref="LicenseKeyData.TryVerifySignature"/> on all of them.
    /// </para>
    /// <para>
    /// Storing such a key in the group of the version that introduces the authority keeps it away from the versions
    /// that cannot use it, but only when it was registered through us. A key given in an environment variable, in a
    /// project file or leased from a license server arrives without passing through the registration, so the reader
    /// has to survive it on its own.
    /// </para>
    /// </remarks>
    [Fact]
    public void AKeyOfAnUnknownAuthorityIsRefusedWithAReason()
    {
        var signedKey = CreateBuilder().SignAndSerialize( TestLicensingAuthorityProvider.DsaTestAuthority );
        Assert.True( LicenseKeyData.TryDeserialize( signedKey, out var licenseKeyData, out var parseError ), parseError );

        // A provider that holds no key at all stands for a version released before the authority that signed this
        // key existed.
        var authorityProvider = new ExplicitLicensingAuthorityProvider();

        Assert.DoesNotContain( TestLicensingAuthorityProvider.DsaTestKeyId, authorityProvider.KeyIds );
        Assert.False( licenseKeyData.TryVerifySignature( authorityProvider, out var errorMessage ) );
        Assert.False( string.IsNullOrWhiteSpace( errorMessage ) );
    }
}
