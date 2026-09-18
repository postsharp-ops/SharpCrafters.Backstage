// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Licensing;
using SharpCrafters.Backstage.Licensing.Licenses;
using SharpCrafters.Backstage.Licensing.Licenses.LicenseFields;
using SharpCrafters.Backstage.Testing;
using System;
using System.Globalization;
using System.Linq;
using Xunit;
using Xunit.Abstractions;

namespace SharpCrafters.Backstage.Tests.Licensing.Licenses;

/// <summary>
/// Tests that a license key carrying a field which the reader does not know survives being read and written again.
/// </summary>
/// <remarks>
/// <para>
/// This is the property that lets a license key be issued today and consumed by a version released before the field
/// in it existed. A reader that loses an unknown field cannot verify the signature, because the signature is taken
/// over the bytes of every field; a reader that rejects one refuses a license the user has paid for. Both failures
/// are discovered by the customer rather than by us, on a version we can no longer change, which is why the format
/// is tested here rather than only through the keys the generator happens to produce today.
/// </para>
/// <para>
/// The two indices used here are reserved for exactly this purpose and are never issued:
/// <see cref="_unknownMustUnderstandField"/> stands for a field a later version adds and requires, and
/// <see cref="_unknownOptionalField"/> for one it adds and does not require. Both are prefixed by their length, so
/// this reader can step over either; what separates them is whether it is then allowed to go on.
/// </para>
/// </remarks>
public sealed class LicenseKeyFieldTests : TestsBase
{
    /// <summary>
    /// The index that <see cref="LicenseKeyDataBuilder.UnknownMustUnderstandField"/> writes to. It is below the
    /// must-understand boundary of 128, so a reader that does not know it must refuse the key.
    /// </summary>
    private const LicenseFieldIndex _unknownMustUnderstandField = (LicenseFieldIndex) 128;

    /// <summary>
    /// The index that <see cref="LicenseKeyDataBuilder.UnknownOptionalField"/> writes to. It is between 129 and 253,
    /// so a reader that does not know it must step over it and carry on.
    /// </summary>
    private const LicenseFieldIndex _unknownOptionalField = (LicenseFieldIndex) 253;

    private static readonly TestLicenseKeyProvider _licenseKeyProvider = new();

    private readonly ILicensingAuthorityProvider _authorityProvider = new TestLicensingAuthorityProvider();

    public LicenseKeyFieldTests( ITestOutputHelper logger ) : base( logger ) { }

    /// <summary>
    /// Creates a builder of a license key of a product that is not a PostSharp one, so that the minimal version is
    /// not stamped into the key and the field under test is the only thing that varies. The stamping has tests of
    /// its own in <see cref="MinPostSharpVersionStampTests"/>.
    /// </summary>
    private static LicenseKeyDataBuilder CreateBuilder()
        => new()
        {
            LicenseId = 900,
            Product = LicenseProduct.MetalamaProfessional,
            LicenseType = LicenseType.Business,
            Generation = LicenseGeneration.Current,
            SubscriptionEndDate = _licenseKeyProvider.DefaultSubscriptionExpirationDate
        };

    /// <summary>
    /// Signs a license key, reads it back, and asserts that nothing about it changed on the way.
    /// </summary>
    /// <param name="configure">Sets the field under test.</param>
    /// <param name="useECDsaAuthority">
    /// Signs with the Elliptic Curve DSA authority rather than the finite field one. Its signature is 64 bytes
    /// against 40, so it moves every field that follows it and is worth running each case against.
    /// </param>
    /// <remarks>
    /// The two assertions answer different questions. That the signature still verifies proves that the bytes the
    /// signature covers came back in the same order with the same content, which a reader that silently dropped the
    /// unknown field could not manage. That the key serializes to the same string proves the same of the bytes the
    /// signature does not cover, which is the signature itself.
    /// </remarks>
    private LicenseKeyData AssertRoundTrip( Action<LicenseKeyDataBuilder> configure, bool useECDsaAuthority = false )
    {
        var builder = CreateBuilder();
        configure( builder );

        var authority = useECDsaAuthority
            ? TestLicensingAuthorityProvider.ECDsaTestAuthority
            : TestLicensingAuthorityProvider.DsaTestAuthority;

        var licenseKey = builder.SignAndSerialize( authority );

        Assert.True( LicenseKeyData.TryDeserialize( licenseKey, out var licenseKeyData, out var parseError ), parseError );
        Assert.True( licenseKeyData.TryVerifySignature( this._authorityProvider, out var signatureError ), signatureError );
        Assert.Equal( licenseKey, licenseKeyData.ToBuilder().Serialize() );

        return licenseKeyData;
    }

    /// <summary>
    /// Reads a license key back with a field of the given value at both reserved indices, and asserts that the
    /// reader kept the field it is not allowed to understand and the one it is not required to.
    /// </summary>
    private void AssertFieldSurvives( object value, bool useECDsaAuthority )
    {
        var optional = this.AssertRoundTrip( builder => builder.UnknownOptionalField = value, useECDsaAuthority );
        Assert.True( optional.HasField( _unknownOptionalField ), "The optional field was lost." );

        var mustUnderstand = this.AssertRoundTrip( builder => builder.UnknownMustUnderstandField = value, useECDsaAuthority );
        Assert.True( mustUnderstand.HasField( _unknownMustUnderstandField ), "The must-understand field was lost." );
    }

    [Theory]
    [InlineData( false )]
    [InlineData( true )]
    public void ABooleanFieldSurvives( bool useECDsaAuthority )
    {
        this.AssertFieldSurvives( false, useECDsaAuthority );
        this.AssertFieldSurvives( true, useECDsaAuthority );
    }

    [Theory]
    [InlineData( false )]
    [InlineData( true )]
    public void AByteFieldSurvives( bool useECDsaAuthority )
    {
        this.AssertFieldSurvives( byte.MinValue, useECDsaAuthority );
        this.AssertFieldSurvives( (byte) (byte.MaxValue / 2), useECDsaAuthority );
        this.AssertFieldSurvives( byte.MaxValue, useECDsaAuthority );
    }

    [Theory]
    [InlineData( false )]
    [InlineData( true )]
    public void AnInt16FieldSurvives( bool useECDsaAuthority )
    {
        this.AssertFieldSurvives( short.MinValue, useECDsaAuthority );
        this.AssertFieldSurvives( (short) (short.MaxValue / 2), useECDsaAuthority );
        this.AssertFieldSurvives( short.MaxValue, useECDsaAuthority );
    }

    [Theory]
    [InlineData( false )]
    [InlineData( true )]
    public void AnInt32FieldSurvives( bool useECDsaAuthority )
    {
        this.AssertFieldSurvives( int.MinValue, useECDsaAuthority );
        this.AssertFieldSurvives( int.MaxValue / 2, useECDsaAuthority );
        this.AssertFieldSurvives( int.MaxValue, useECDsaAuthority );
    }

    [Theory]
    [InlineData( false )]
    [InlineData( true )]
    public void AnInt64FieldSurvives( bool useECDsaAuthority )
    {
        this.AssertFieldSurvives( long.MinValue, useECDsaAuthority );
        this.AssertFieldSurvives( long.MaxValue / 2, useECDsaAuthority );
        this.AssertFieldSurvives( long.MaxValue, useECDsaAuthority );
    }

    /// <summary>
    /// A date and time field. The value is written as the number of ticks of the moment in universal time, so the
    /// timezone of the machine that reads the key does not change what comes back.
    /// </summary>
    [Theory]
    [InlineData( "2010-01-01T00:00:00Z" )]
    [InlineData( "2026-09-18T14:37:11Z" )]
    [InlineData( "2189-12-31T23:59:59Z" )]
    public void ADateTimeFieldSurvives( string value )
    {
        var dateTime = DateTime.Parse( value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal );

        this.AssertFieldSurvives( dateTime, false );
        this.AssertFieldSurvives( dateTime, true );
    }

    /// <summary>
    /// A string field, including the shapes that a length-prefixed encoding is most likely to get wrong: nothing at
    /// all, embedded null characters, and characters that take more than one byte.
    /// </summary>
    [Theory]
    [InlineData( "" )]
    [InlineData( "\0" )]
    [InlineData( "\0\0\0\0\0\0\0" )]
    [InlineData( " " )]
    [InlineData( "              " )]
    [InlineData( "příliš žluťoučký kůň úpěl ďábelské ódy" )]
    [InlineData( "слишком желтоватый конь вой дьявольская ода" )]
    [InlineData( "日本語のテキスト" )]
    public void AStringFieldSurvives( string value )
    {
        this.AssertFieldSurvives( value, false );
        this.AssertFieldSurvives( value, true );
    }

    /// <summary>
    /// A string of the greatest length the encoding allows, which is what a field holding a licensee name is most
    /// likely to reach.
    /// </summary>
    [Fact]
    public void AStringFieldOfTheGreatestLengthSurvives() => this.AssertFieldSurvives( new string( 'x', 255 ), false );

    /// <summary>
    /// The length of a string field is counted in bytes and not in characters, although the message says otherwise.
    /// A name of 200 accented characters is therefore refused where a name of 200 unaccented ones is accepted.
    /// </summary>
    /// <remarks>
    /// This is asserted rather than corrected because the limit is a property of the format: the length is written
    /// as one byte, so 255 bytes is all there is room for. What the test pins is that the limit is reached at the
    /// byte count, so that a caller who trusts the message is contradicted here rather than by a customer.
    /// </remarks>
    [Fact]
    public void AStringFieldIsLimitedByItsLengthInBytes()
    {
        // 255 characters that take one byte each: the greatest string the format holds.
        Assert.Equal( 255, new string( 'x', 255 ).Length );
        this.AssertRoundTrip( builder => builder.UnknownOptionalField = new string( 'x', 255 ) );

        // One more character of one byte is one byte too many.
        Assert.Throws<InvalidOperationException>( () => this.AssertRoundTrip( builder => builder.UnknownOptionalField = new string( 'x', 256 ) ) );

        // And 128 characters of two bytes each are also 256 bytes, although they are half as many characters.
        Assert.Throws<InvalidOperationException>( () => this.AssertRoundTrip( builder => builder.UnknownOptionalField = new string( 'é', 128 ) ) );
    }

    /// <summary>
    /// A buffer field, at the lengths where an off-by-one in the length prefix would show.
    /// </summary>
    [Theory]
    [InlineData( 0 )]
    [InlineData( 1 )]
    [InlineData( 2 )]
    [InlineData( 127 )]
    [InlineData( 255 )]
    public void AByteArrayFieldSurvives( int length )
    {
        var bytes = Enumerable.Range( 0, length ).Select( i => (byte) i ).ToArray();

        this.AssertFieldSurvives( bytes, false );
        this.AssertFieldSurvives( bytes, true );
    }

    /// <summary>
    /// A buffer longer than the length prefix can count is refused rather than silently truncated, which would
    /// produce a key whose signature cannot verify.
    /// </summary>
    [Fact]
    public void AByteArrayFieldLongerThanTheFormatAllowsIsRefused()
        => Assert.Throws<InvalidOperationException>( () => this.AssertRoundTrip( builder => builder.UnknownOptionalField = new byte[256] ) );

    [Theory]
    [InlineData( false )]
    [InlineData( true )]
    public void AGuidFieldSurvives( bool useECDsaAuthority )
    {
        this.AssertFieldSurvives( Guid.Empty, useECDsaAuthority );
        this.AssertFieldSurvives( new Guid( "6f37a1bb-0f4e-4b8f-9a07-6b0b0b3f4c21" ), useECDsaAuthority );
    }

    /// <summary>
    /// A field that the reader is not required to understand leaves the key consumable.
    /// </summary>
    [Fact]
    public void AnUnknownOptionalFieldLeavesTheKeyValid()
    {
        var licenseKeyData = this.AssertRoundTrip( builder => builder.UnknownOptionalField = 42 );

        Assert.True( licenseKeyData.ValidateFields( out var errorMessage ), errorMessage );
    }

    /// <summary>
    /// A field that the reader is required to understand and does not makes the key unusable, which is the whole
    /// point of the distinction: a later version can add a field that changes what the key grants, and be sure that
    /// an earlier version refuses the key rather than granting the wrong thing.
    /// </summary>
    [Fact]
    public void AnUnknownMustUnderstandFieldMakesTheKeyInvalid()
    {
        var licenseKeyData = this.AssertRoundTrip( builder => builder.UnknownMustUnderstandField = 42 );

        Assert.False( licenseKeyData.ValidateFields( out var errorMessage ) );
        Assert.Equal( "the license key contains unknown must-understand fields", errorMessage );
    }

    /// <summary>
    /// A license type that this version does not know makes the key unusable. The type decides what the key grants,
    /// so a version that cannot name it cannot be trusted to honour it.
    /// </summary>
    [Fact]
    public void AnUnknownLicenseTypeMakesTheKeyInvalid()
    {
        var licenseKeyData = this.AssertRoundTrip( builder => builder.LicenseType = (LicenseType) 250 );

        Assert.False( licenseKeyData.ValidateFields( out var errorMessage ) );
        Assert.Equal( "the license key license type is unknown", errorMessage );
    }

    /// <summary>
    /// A product that this version does not know makes the key unusable, for the same reason.
    /// </summary>
    [Fact]
    public void AnUnknownProductMakesTheKeyInvalid()
    {
        var licenseKeyData = this.AssertRoundTrip( builder => builder.Product = (LicenseProduct) 250 );

        Assert.False( licenseKeyData.ValidateFields( out var errorMessage ) );
        Assert.Equal( "the license key licensed product is unknown", errorMessage );
    }

    /// <summary>
    /// An unknown type and an unknown product still read and write unchanged, although they make the key unusable.
    /// A reader that could not read such a key could not report why it refuses it.
    /// </summary>
    [Fact]
    public void AKeyThatIsInvalidIsStillReadable()
    {
        var licenseKeyData = this.AssertRoundTrip(
            builder =>
            {
                builder.LicenseType = (LicenseType) 250;
                builder.Product = (LicenseProduct) 251;
                builder.UnknownMustUnderstandField = "a field of a later version";
            } );

        Assert.Equal( (LicenseType) 250, licenseKeyData.LicenseType );
        Assert.Equal( (LicenseProduct) 251, licenseKeyData.Product );
        Assert.True( licenseKeyData.HasField( _unknownMustUnderstandField ) );
    }

    /// <summary>
    /// Several unknown fields at once, which is what a key issued two versions ahead looks like.
    /// </summary>
    [Fact]
    public void SeveralUnknownFieldsSurviveTogether()
    {
        var licenseKeyData = this.AssertRoundTrip(
            builder =>
            {
                builder.UnknownMustUnderstandField = new byte[] { 1, 2, 3 };
                builder.UnknownOptionalField = "a field of a later version";
            } );

        Assert.True( licenseKeyData.HasField( _unknownMustUnderstandField ) );
        Assert.True( licenseKeyData.HasField( _unknownOptionalField ) );
    }

    /// <summary>
    /// A key carrying an unknown field is one that a reader released before that field existed must refuse or step
    /// over, and this is what tells the two apart, so it is asserted beside the fields themselves.
    /// </summary>
    [Fact]
    public void AnUnknownFieldIsALengthPrefixedField()
    {
        Assert.True( this.AssertRoundTrip( builder => builder.UnknownOptionalField = 1 ).HasLengthPrefixedField );
        Assert.True( this.AssertRoundTrip( builder => builder.UnknownMustUnderstandField = 1 ).HasLengthPrefixedField );
    }
}
