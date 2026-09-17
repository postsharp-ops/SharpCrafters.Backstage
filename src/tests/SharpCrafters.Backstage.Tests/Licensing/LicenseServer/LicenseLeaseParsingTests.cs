// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Licensing.LicenseServer;
using System;
using Xunit;

namespace SharpCrafters.Backstage.Tests.Licensing.LicenseServer;

/// <summary>
/// Tests the parsing of the response body of the <c>Lease.ashx</c> handler of a license server.
/// </summary>
/// <remarks>
/// The wire format is an external contract: the servers that produce it are deployed by customers and we cannot
/// change them. These tests therefore pin literal strings rather than round-tripping through a serializer of our own,
/// which would agree with itself whatever the format really is.
/// </remarks>
public sealed class LicenseLeaseParsingTests
{
    /// <summary>
    /// An arbitrary but fixed instant, standing for the clock of the client while it parses.
    /// </summary>
    private static readonly DateTime _now = new( 2026, 6, 1, 12, 0, 0, DateTimeKind.Utc );

    private const string _licenseKey =
        "3-ZEQQQQQQZTQEQCRCE4UW3UFEB4URXMHRB8KQBJJSB64LX7EAEJZWKEM8SCXJK6KJLFD92CAJFQKCGC67A9NVYA2JGNEHLB8QQG4JAF94J58KUJQZW8ZQQDTFJJPA";

    private static LicenseLease Parse( string body )
    {
        Assert.True( LicenseLease.TryDeserialize( body, _now, out var lease ), "The body did not parse: " + body );

        return lease;
    }

    /// <summary>
    /// Tests a response of the exact shape that the <c>Serialize</c> method of the license server produces. This is
    /// the anchor of the whole format: every other test here builds its input from an assumption about the format,
    /// whereas this one states the format itself.
    /// </summary>
    /// <remarks>
    /// The separator is a semicolon followed by a space, the name and the value of a part are separated by a colon
    /// followed by a space, and the instants are written by
    /// <c>XmlConvert.ToString( value, XmlDateTimeSerializationMode.Utc )</c>, which produces seven fractional digits
    /// and a <c>Z</c> suffix.
    /// </remarks>
    [Fact]
    public void GoldenResponseParses()
    {
        var lease = Parse(
            "License: " + _licenseKey
                        + "; StartTime: 2026-09-17T08:14:22.1234567Z; EndTime: 2026-09-20T08:14:22.1234567Z; RenewTime: 2026-09-19T08:14:22.1234567Z" );

        Assert.Equal( _licenseKey, lease.LicenseKey );
        Assert.Equal( new DateTime( 2026, 9, 17, 8, 14, 22, DateTimeKind.Utc ).AddTicks( 1234567 ), lease.StartTime );
        Assert.Equal( new DateTime( 2026, 9, 20, 8, 14, 22, DateTimeKind.Utc ).AddTicks( 1234567 ), lease.EndTime );
        Assert.Equal( new DateTime( 2026, 9, 19, 8, 14, 22, DateTimeKind.Utc ).AddTicks( 1234567 ), lease.RenewTime );
    }

    /// <summary>
    /// Tests that the instants are kept in UTC. PostSharp converted them to local time and compared them to a local
    /// clock, which cancels out only when the client and the server are in the same time zone.
    /// </summary>
    [Fact]
    public void InstantsAreUtc()
    {
        var lease = Parse( "License: KEY; StartTime: 2026-06-01T12:00:00Z" );

        Assert.Equal( DateTimeKind.Utc, lease.StartTime.Kind );
        Assert.Equal( DateTimeKind.Utc, lease.EndTime.Kind );
        Assert.Equal( DateTimeKind.Utc, lease.RenewTime.Kind );
    }

    /// <summary>
    /// Tests that an instant written with an offset rather than in UTC is converted, not taken at face value.
    /// </summary>
    [Fact]
    public void OffsetInstantIsConvertedToUtc()
    {
        var lease = Parse( "License: KEY; StartTime: 2026-06-01T14:00:00+02:00" );

        Assert.Equal( new DateTime( 2026, 6, 1, 12, 0, 0, DateTimeKind.Utc ), lease.StartTime );
    }

    [Fact]
    public void MissingStartTimeDefaultsToNow()
    {
        var lease = Parse( "License: KEY; EndTime: 2026-06-03T12:00:00Z" );

        Assert.Equal( _now, lease.StartTime );
    }

    [Fact]
    public void MissingEndTimeDefaultsToOneDayAfterStart()
    {
        var lease = Parse( "License: KEY; StartTime: 2026-06-01T08:00:00Z" );

        Assert.Equal( new DateTime( 2026, 6, 2, 8, 0, 0, DateTimeKind.Utc ), lease.EndTime );
    }

    [Fact]
    public void MissingRenewTimeDefaultsToEndTime()
    {
        var lease = Parse( "License: KEY; EndTime: 2026-06-03T12:00:00Z" );

        Assert.Equal( lease.EndTime, lease.RenewTime );
    }

    /// <summary>
    /// Tests that the three defaults chain when a response carries nothing but the licence key, which is what a
    /// minimal server implementation returns.
    /// </summary>
    [Fact]
    public void AllDefaultsChain()
    {
        var lease = Parse( "License: KEY" );

        Assert.Equal( _now, lease.StartTime );
        Assert.Equal( _now.AddDays( 1 ), lease.EndTime );
        Assert.Equal( _now.AddDays( 1 ), lease.RenewTime );
    }

    [Theory]
    [InlineData( "license" )]
    [InlineData( "LICENSE" )]
    [InlineData( "LiCeNsE" )]
    public void PartNameIsCaseInsensitive( string name )
    {
        var lease = Parse( name + ": KEY" );

        Assert.Equal( "KEY", lease.LicenseKey );
    }

    /// <summary>
    /// Tests that a part the running version does not know is ignored, so that a server of a later version can add
    /// one without breaking every deployed client.
    /// </summary>
    [Fact]
    public void UnknownPartIsIgnored()
    {
        var lease = Parse( "License: KEY; Seats: 3; Foo: bar; EndTime: 2026-06-03T12:00:00Z" );

        Assert.Equal( "KEY", lease.LicenseKey );
        Assert.Equal( new DateTime( 2026, 6, 3, 12, 0, 0, DateTimeKind.Utc ), lease.EndTime );
    }

    [Theory]
    [InlineData( "License: KEY; nonsense" )]
    [InlineData( "License: KEY; EndTime:" )]
    [InlineData( "License: KEY;" )]
    [InlineData( "License: KEY; ; ;" )]
    public void MalformedPartIsIgnored( string body )
    {
        var lease = Parse( body );

        Assert.Equal( "KEY", lease.LicenseKey );
    }

    /// <summary>
    /// Tests that a part is split at its first colon, so that the colons inside a timestamp stay in the value.
    /// </summary>
    [Fact]
    public void ValueKeepsItsOwnColons()
    {
        var lease = Parse( "License: KEY; StartTime: 2026-06-01T12:34:56Z" );

        Assert.Equal( new DateTime( 2026, 6, 1, 12, 34, 56, DateTimeKind.Utc ), lease.StartTime );
    }

    [Fact]
    public void SurroundingWhitespaceIsTrimmed()
    {
        var lease = Parse( "  License :  KEY  ;   StartTime :  2026-06-01T08:00:00Z  " );

        Assert.Equal( "KEY", lease.LicenseKey );
        Assert.Equal( new DateTime( 2026, 6, 1, 8, 0, 0, DateTimeKind.Utc ), lease.StartTime );
    }

    /// <summary>
    /// Tests that a body broken over several lines parses, which is what a handler that writes a newline produces.
    /// </summary>
    [Fact]
    public void LineBreaksAreTolerated()
    {
        var lease = Parse( "License: KEY;\r\nEndTime: 2026-06-03T12:00:00Z\r\n" );

        Assert.Equal( "KEY", lease.LicenseKey );
        Assert.Equal( new DateTime( 2026, 6, 3, 12, 0, 0, DateTimeKind.Utc ), lease.EndTime );
    }

    /// <summary>
    /// Tests that the last occurrence of a repeated part wins, so that the outcome is defined rather than incidental.
    /// </summary>
    [Fact]
    public void RepeatedPartTakesTheLastValue()
    {
        var lease = Parse( "License: KEY; EndTime: 2026-06-03T12:00:00Z; EndTime: 2026-06-04T12:00:00Z" );

        Assert.Equal( new DateTime( 2026, 6, 4, 12, 0, 0, DateTimeKind.Utc ), lease.EndTime );
    }

    /// <summary>
    /// Tests the bodies that carry no licence key, which is the only mandatory part. A proxy login page and an empty
    /// response both land here, so the failure must be a returned <see langword="false"/> and never an exception.
    /// </summary>
    [Theory]
    [InlineData( null )]
    [InlineData( "" )]
    [InlineData( "   " )]
    [InlineData( "<html><head><title>Sign in</title></head><body>Proxy authentication required.</body></html>" )]
    [InlineData( "StartTime: 2026-06-01T12:00:00Z; EndTime: 2026-06-03T12:00:00Z" )]
    [InlineData( "License:" )]
    [InlineData( "no colon here" )]
    public void BodyWithoutLicenseKeyFails( string? body )
    {
        Assert.False( LicenseLease.TryDeserialize( body, _now, out var lease ) );
        Assert.Null( lease );
    }

    /// <summary>
    /// Tests that an unparsable or out-of-range instant fails the whole body instead of throwing. PostSharp caught
    /// only <see cref="FormatException"/>, so a value out of the range of <see cref="DateTime"/> escaped its handler.
    /// </summary>
    [Theory]
    [InlineData( "License: KEY; StartTime: not-a-date" )]
    [InlineData( "License: KEY; EndTime: 2026-13-45T99:99:99Z" )]
    [InlineData( "License: KEY; RenewTime: 99999-01-01T00:00:00Z" )]
    [InlineData( "License: KEY; StartTime: 0" )]
    public void MalformedInstantFails( string body )
    {
        Assert.False( LicenseLease.TryDeserialize( body, _now, out var lease ) );
        Assert.Null( lease );
    }

    /// <summary>
    /// Tests that a licence key of an unusual length is not truncated, since the format has no length field.
    /// </summary>
    [Fact]
    public void LongLicenseKeyParses()
    {
        var longKey = new string( 'A', 4096 );
        var lease = Parse( "License: " + longKey );

        Assert.Equal( longKey, lease.LicenseKey );
    }

    /// <summary>
    /// Tests that two leases carrying the same values compare equal, which is what lets a renewal that changes
    /// nothing avoid a write to the lease store.
    /// </summary>
    [Fact]
    public void EqualLeasesCompareEqual()
    {
        const string body = "License: KEY; StartTime: 2026-06-01T08:00:00Z; EndTime: 2026-06-04T08:00:00Z; RenewTime: 2026-06-03T08:00:00Z";

        Assert.Equal( Parse( body ), Parse( body ) );
    }
}
