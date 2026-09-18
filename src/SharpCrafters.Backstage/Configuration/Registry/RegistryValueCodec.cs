// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using JetBrains.Annotations;
using System;
using System.Globalization;

namespace SharpCrafters.Backstage.Configuration.Registry;

/// <summary>
/// Converts between the values of a configuration object and the way PostSharp 2026.0 encodes them in the registry.
/// </summary>
/// <remarks>
/// Every encoding here is dictated by <c>RegistryKeyExtensions</c> of PostSharp 2026.0, which reads and writes the
/// same values beside this version. None of them may be changed.
/// </remarks>
[PublicAPI]
public static class RegistryValueCodec
{
    /// <summary>
    /// The instant from which a date is counted. It is a local-time-agnostic constant of PostSharp 2026.0 and is
    /// interpreted as UTC.
    /// </summary>
    private static readonly DateTime _referenceDate = new( 2000, 1, 1, 0, 0, 0, DateTimeKind.Utc );

    /// <summary>
    /// The largest value that is read as a date. PostSharp 2026.0 computes it as one thousand years of milliseconds
    /// and treats anything above it, or anything not positive, as an absent value rather than as a date.
    /// </summary>
    private const long _maxDateValue = 1000L * 60 * 60 * 24 * 365 * 1000;

    /// <summary>
    /// Encodes a date as the <c>QWORD</c> that PostSharp 2026.0 stores: the number of whole milliseconds since
    /// 2000-01-01 UTC.
    /// </summary>
    public static long DateTimeToQWord( DateTime value ) => (long) (value.ToUniversalTime() - _referenceDate).TotalMilliseconds;

    /// <summary>
    /// Decodes the <c>QWORD</c> of a date.
    /// </summary>
    /// <param name="value">The stored value, or <see langword="null"/> when the value is absent.</param>
    /// <returns>
    /// The date, in local time as PostSharp 2026.0 returns it, or <see langword="null"/> when the value is absent or
    /// outside the range that PostSharp 2026.0 accepts.
    /// </returns>
    public static DateTime? QWordToDateTime( object? value )
    {
        if ( ToInt64( value ) is not { } timestamp || timestamp <= 0 || timestamp > _maxDateValue )
        {
            return null;
        }

        return _referenceDate.AddMilliseconds( timestamp ).ToLocalTime();
    }

    /// <summary>
    /// Encodes a Boolean as the <c>DWORD</c> that PostSharp 2026.0 stores.
    /// </summary>
    public static int BooleanToDWord( bool value ) => value ? 1 : 0;

    /// <summary>
    /// Decodes the <c>DWORD</c> of a Boolean.
    /// </summary>
    /// <param name="value">The stored value, or <see langword="null"/> when the value is absent.</param>
    /// <param name="defaultValue">
    /// What an absent value means. It is not always <see langword="false"/>: several settings of PostSharp 2026.0,
    /// such as <c>WarnAboutSubscriptionExpiration</c>, are on unless they were turned off.
    /// </param>
    public static bool DWordToBoolean( object? value, bool defaultValue = false ) => ToInt32( value ) is { } number ? number != 0 : defaultValue;

    /// <summary>
    /// Encodes a Boolean that may be unset as the <c>DWORD</c> that PostSharp 2026.0 stores: <c>0</c> for unset,
    /// <c>1</c> for true and <c>2</c> for false. This is not the encoding of <see cref="BooleanToDWord"/>, in which
    /// <c>0</c> is false.
    /// </summary>
    public static int NullableBooleanToDWord( bool? value ) => value switch { true => 1, false => 2, null => 0 };

    /// <summary>
    /// Decodes the <c>DWORD</c> of a Boolean that may be unset. Any value other than <c>1</c> and <c>2</c> is unset.
    /// </summary>
    public static bool? DWordToNullableBoolean( object? value )
        => ToInt32( value ) switch { 1 => true, 2 => false, _ => null };

    /// <summary>
    /// Reads a value as a string, which is <see langword="null"/> when the value is absent. A value stored with
    /// another kind is not converted: it is not a string and is reported as absent, as PostSharp 2026.0 does by
    /// casting.
    /// </summary>
    public static string? ToStringValue( object? value ) => value as string;

    /// <summary>
    /// Reads a value as a <c>DWORD</c>, tolerating one stored as a <c>QWORD</c> or as a string, which an earlier
    /// version or a hand edit may have left.
    /// </summary>
    private static int? ToInt32( object? value )
        => value switch
        {
            int number => number,
            long number when number is >= int.MinValue and <= int.MaxValue => (int) number,
            string text when int.TryParse( text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed ) => parsed,
            _ => null
        };

    /// <summary>
    /// Reads a value as a <c>QWORD</c>, tolerating one stored as a <c>DWORD</c> or as a string.
    /// </summary>
    private static long? ToInt64( object? value )
        => value switch
        {
            long number => number,
            int number => number,
            string text when long.TryParse( text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed ) => parsed,
            _ => null
        };
}
