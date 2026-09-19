// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Configuration.Registry;
using System;
using Xunit;

namespace SharpCrafters.Backstage.Tests.Registry;

/// <summary>
/// Pins the encodings by which a configuration value is stored in the registry. PostSharp 2026.0 reads and writes
/// the same values beside this version, so an encoding that drifts is not a bug this product notices: it is a
/// setting that the other version silently misreads. Each expected number is therefore computed by hand from the
/// rules of <c>RegistryKeyExtensions</c> rather than from the code under test.
/// </summary>
public sealed class RegistryValueConvertersTests
{
    /// <summary>
    /// A date counts milliseconds from 2000-01-01 UTC.
    /// </summary>
    [Fact]
    public void DateIsCountedFromTheYear2000InUtc()
    {
        Assert.Equal( 0L, RegistryValueConverters.DateTimeToQWord( new DateTime( 2000, 1, 1, 0, 0, 0, DateTimeKind.Utc ) ) );
        Assert.Equal( 1000L, RegistryValueConverters.DateTimeToQWord( new DateTime( 2000, 1, 1, 0, 0, 1, DateTimeKind.Utc ) ) );
        Assert.Equal( 86_400_000L, RegistryValueConverters.DateTimeToQWord( new DateTime( 2000, 1, 2, 0, 0, 0, DateTimeKind.Utc ) ) );

        // 2026-09-18 is 9757 days after the reference date.
        Assert.Equal( 9757L * 86_400_000L, RegistryValueConverters.DateTimeToQWord( new DateTime( 2026, 9, 18, 0, 0, 0, DateTimeKind.Utc ) ) );
    }

    /// <summary>
    /// A date written by this version reads back as the same instant, whichever kind it was given. PostSharp 2026.0
    /// returns a local time, so the comparison is made on the instant and not on the wall clock.
    /// </summary>
    [Theory]
    [InlineData( DateTimeKind.Utc )]
    [InlineData( DateTimeKind.Local )]
    public void ADateSurvivesARoundTrip( DateTimeKind kind )
    {
        var value = DateTime.SpecifyKind( new DateTime( 2026, 9, 18, 14, 35, 46 ), kind );

        var decoded = RegistryValueConverters.QWordToDateTime( RegistryValueConverters.DateTimeToQWord( value ) );

        Assert.NotNull( decoded );
        Assert.Equal( value.ToUniversalTime(), decoded.Value.ToUniversalTime() );
    }

    /// <summary>
    /// A decoded date is in universal time, which is what the stored number counts from.
    /// </summary>
    /// <remarks>
    /// PostSharp 2026.0 converts to local time here, and may: it compares the result against a local clock. This
    /// version compares against <c>IDateTimeProvider.UtcNow</c>, and the arithmetic of a <see cref="DateTime"/>
    /// ignores its kind, so a value converted to local time is read as an instant offset by the time zone. The
    /// stored number is untouched either way, so the two versions still agree on what is written.
    /// </remarks>
    [Fact]
    public void ADecodedDateIsUniversal()
    {
        var decoded = RegistryValueConverters.QWordToDateTime( 86_400_000L )!.Value;

        Assert.Equal( DateTimeKind.Utc, decoded.Kind );
        Assert.Equal( new DateTime( 2000, 1, 2, 0, 0, 0, DateTimeKind.Utc ), decoded );
    }

    /// <summary>
    /// A value outside the range that PostSharp 2026.0 accepts means that there is no date, not that the date is
    /// the one the number would give. Zero in particular is how an absent date reads, because the value is absent
    /// from the key and the default of the read is zero.
    /// </summary>
    [Theory]
    [InlineData( 0L )]
    [InlineData( -1L )]
    [InlineData( long.MinValue )]
    [InlineData( (1000L * 60 * 60 * 24 * 365 * 1000) + 1 )]
    [InlineData( long.MaxValue )]
    public void ADateOutsideTheAcceptedRangeIsAbsent( long stored ) => Assert.Null( RegistryValueConverters.QWordToDateTime( stored ) );

    /// <summary>
    /// The largest accepted value is a date, so the bound is inclusive as it is in PostSharp 2026.0.
    /// </summary>
    [Fact]
    public void TheLargestAcceptedDateIsADate() => Assert.NotNull( RegistryValueConverters.QWordToDateTime( 1000L * 60 * 60 * 24 * 365 * 1000 ) );

    [Fact]
    public void AnAbsentDateIsNull() => Assert.Null( RegistryValueConverters.QWordToDateTime( null ) );

    [Fact]
    public void ABooleanIsZeroOrOne()
    {
        Assert.Equal( 1, RegistryValueConverters.BooleanToDWord( true ) );
        Assert.Equal( 0, RegistryValueConverters.BooleanToDWord( false ) );
    }

    /// <summary>
    /// Any value other than zero is true, as a C conversion would have it.
    /// </summary>
    [Theory]
    [InlineData( 1, true )]
    [InlineData( 2, true )]
    [InlineData( -1, true )]
    [InlineData( 0, false )]
    public void ABooleanIsTrueWhenItIsNotZero( int stored, bool expected ) => Assert.Equal( expected, RegistryValueConverters.DWordToBoolean( stored ) );

    /// <summary>
    /// Several settings of PostSharp 2026.0 are on unless they were turned off, so the default of an absent value is
    /// not always false and the caller states it.
    /// </summary>
    [Theory]
    [InlineData( true )]
    [InlineData( false )]
    public void AnAbsentBooleanTakesTheDefaultOfTheCaller( bool defaultValue )
        => Assert.Equal( defaultValue, RegistryValueConverters.DWordToBoolean( null, defaultValue ) );

    /// <summary>
    /// A Boolean that may be unset uses a different encoding from a plain one: zero is unset rather than false, and
    /// false is two. Writing one where the other is expected turns "no" into "not answered".
    /// </summary>
    [Fact]
    public void AnUnsetBooleanIsZeroAndFalseIsTwo()
    {
        Assert.Equal( 0, RegistryValueConverters.NullableBooleanToDWord( null ) );
        Assert.Equal( 1, RegistryValueConverters.NullableBooleanToDWord( true ) );
        Assert.Equal( 2, RegistryValueConverters.NullableBooleanToDWord( false ) );
    }

    [Theory]
    [InlineData( 1, true )]
    [InlineData( 2, false )]
    [InlineData( 0, null )]
    [InlineData( 3, null )]
    public void ANullableBooleanDecodesOnlyOneAndTwo( int stored, bool? expected )
        => Assert.Equal( expected, RegistryValueConverters.DWordToNullableBoolean( stored ) );

    [Fact]
    public void AnAbsentNullableBooleanIsUnset() => Assert.Null( RegistryValueConverters.DWordToNullableBoolean( null ) );

    /// <summary>
    /// A value of another kind is not a string. PostSharp 2026.0 casts, so it would throw and fall back to its
    /// default; reporting the value as absent reaches the same default without the exception.
    /// </summary>
    [Fact]
    public void AValueThatIsNotAStringIsAbsent()
    {
        Assert.Equal( "text", RegistryValueConverters.ToStringValue( "text" ) );
        Assert.Null( RegistryValueConverters.ToStringValue( 1 ) );
        Assert.Null( RegistryValueConverters.ToStringValue( null ) );
    }

    /// <summary>
    /// A number stored with the wrong width, which an earlier version or a hand edit may leave, is still read. This
    /// is more tolerant than PostSharp 2026.0, which casts and falls back to its default, and it never turns an
    /// absent value into a present one.
    /// </summary>
    [Fact]
    public void ANumberOfTheWrongWidthIsStillRead()
    {
        Assert.Equal( 86_400_000L, RegistryValueConverters.DateTimeToQWord( RegistryValueConverters.QWordToDateTime( 86_400_000 )!.Value ) );
        Assert.True( RegistryValueConverters.DWordToBoolean( 1L ) );
    }
}
