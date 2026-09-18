// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Licensing.LicenseServer;
using System;
using System.Globalization;
using System.Text;

namespace PostSharp.Backstage.Configuration;

/// <summary>
/// Reads and writes a lease in the textual form that PostSharp 2026.0 keeps in the registry.
/// </summary>
/// <remarks>
/// The form is <c>License: {key}; StartTime: {date}; EndTime: {date}; RenewTime: {date}</c>, with each date in the
/// round-trip format and in universal time. It is the form of <c>LicenseLease.Serialize</c> in that version, and it
/// is what lets a lease acquired by either version be found by the other, so that the two do not take two seats of
/// the same license server for one developer.
/// </remarks>
internal static class PostSharpLeaseSerializer
{
    private const string _licenseField = "License";
    private const string _startTimeField = "StartTime";
    private const string _endTimeField = "EndTime";
    private const string _renewTimeField = "RenewTime";

    public static string Serialize( LeaseConfiguration lease )
    {
        var builder = new StringBuilder();
        AppendField( builder, _licenseField, lease.LicenseKey );
        AppendField( builder, _startTimeField, FormatDate( lease.StartTime ) );
        AppendField( builder, _endTimeField, FormatDate( lease.EndTime ) );
        AppendField( builder, _renewTimeField, FormatDate( lease.RenewTime ) );

        return builder.ToString();
    }

    private static void AppendField( StringBuilder builder, string name, string value )
    {
        if ( builder.Length > 0 )
        {
            builder.Append( "; " );
        }

        builder.Append( name ).Append( ": " ).Append( value );
    }

    private static string FormatDate( DateTime value ) => value.ToUniversalTime().ToString( "O", CultureInfo.InvariantCulture );

    /// <summary>
    /// Parses a lease, or returns <see langword="null"/> when the text is not one.
    /// </summary>
    /// <remarks>
    /// A blank value is what PostSharp 2026.0 leaves behind when it finds a lease that has expired, so it is not an
    /// error and is simply no lease. Anything else that does not parse is not an error either: a lease is derived
    /// state that can be acquired again, so the worst a bad value can cost is one request.
    /// </remarks>
    public static LeaseConfiguration? Deserialize( string? text )
    {
        if ( string.IsNullOrWhiteSpace( text ) )
        {
            return null;
        }

        string? licenseKey = null;
        DateTime? startTime = null, endTime = null, renewTime = null;

        foreach ( var field in text!.Split( ';' ) )
        {
            var separatorIndex = field.IndexOf( ":", StringComparison.Ordinal );

            if ( separatorIndex < 0 )
            {
                continue;
            }

            var name = field.Substring( 0, separatorIndex ).Trim();
            var value = field.Substring( separatorIndex + 1 ).Trim();

            switch ( name )
            {
                case _licenseField:
                    licenseKey = value;

                    break;

                case _startTimeField:
                    startTime = ParseDate( value );

                    break;

                case _endTimeField:
                    endTime = ParseDate( value );

                    break;

                case _renewTimeField:
                    renewTime = ParseDate( value );

                    break;
            }
        }

        if ( string.IsNullOrWhiteSpace( licenseKey ) || startTime == null || endTime == null || renewTime == null )
        {
            return null;
        }

        return new LeaseConfiguration
        {
            LicenseKey = licenseKey!, StartTime = startTime.Value, EndTime = endTime.Value, RenewTime = renewTime.Value
        };
    }

    /// <summary>
    /// Parses a date written in the round-trip form.
    /// </summary>
    /// <remarks>
    /// <see cref="DateTimeStyles.RoundtripKind"/> is used alone, and not with
    /// <see cref="DateTimeStyles.AdjustToUniversal"/>, which it cannot be combined with: it already takes the kind
    /// from the text, so a date written with the trailing <c>Z</c> comes back as a universal one.
    /// </remarks>
    private static DateTime? ParseDate( string value )
        => DateTime.TryParse( value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed ) ? parsed : null;
}
