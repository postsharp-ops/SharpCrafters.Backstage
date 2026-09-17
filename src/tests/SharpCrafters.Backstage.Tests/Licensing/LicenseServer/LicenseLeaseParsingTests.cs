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

    /// <summary>
    /// Tests that a lease granted without a start is taken to start now. The protocol makes only the licence key
    /// mandatory, and a customer whose server omits the rest must still be able to build.
    /// </summary>
    [Fact]
    public void MissingStartTimeDefaultsToNow()
    {
        var lease = Parse( "License: KEY; EndTime: 2026-06-03T12:00:00Z" );

        Assert.Equal( _now, lease.StartTime );
    }

    /// <summary>
    /// Tests that a lease granted without an end lasts a day. It has to end at some point, and a day is short
    /// enough that a seat is given back quickly, yet long enough to cover a working day of builds.
    /// </summary>
    [Fact]
    public void MissingEndTimeDefaultsToOneDayAfterStart()
    {
        var lease = Parse( "License: KEY; StartTime: 2026-06-01T08:00:00Z" );

        Assert.Equal( new DateTime( 2026, 6, 2, 8, 0, 0, DateTimeKind.Utc ), lease.EndTime );
    }

    /// <summary>
    /// Tests that a lease granted without a renewal instant is renewed when it ends. Without a margin the product
    /// renews later than it otherwise would, which is the safe direction: it takes a seat no sooner than the
    /// customer agreed to.
    /// </summary>
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

    /// <summary>
    /// Tests that the product reads the answer of a server whatever case it writes its field names in. The format
    /// is a line of text produced by deployments the customer controls and we do not.
    /// </summary>
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

    /// <summary>
    /// Tests that a stray or empty field does not cost the customer their licence. The licence key is what matters;
    /// discarding the whole lease over a field nobody reads would fail a build for no reason.
    /// </summary>
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
    /// Tests that the instants of a lease survive being read. They are written with colons in them, and a product
    /// that cut a value at the first one would take the expiry of the lease to be an hour it cannot read and would
    /// refuse a licence that was granted.
    /// </summary>
    [Fact]
    public void ValueKeepsItsOwnColons()
    {
        var lease = Parse( "License: KEY; StartTime: 2026-06-01T12:34:56Z" );

        Assert.Equal( new DateTime( 2026, 6, 1, 12, 34, 56, DateTimeKind.Utc ), lease.StartTime );
    }

    /// <summary>
    /// Tests that spacing in the answer of a server does not change the licence key it grants. A key read with a
    /// space around it is a key that does not verify, and the customer would be told their valid licence is
    /// invalid.
    /// </summary>
    [Fact]
    public void SurroundingWhitespaceIsTrimmed()
    {
        var lease = Parse( "  License :  KEY  ;   StartTime :  2026-06-01T08:00:00Z  " );

        Assert.Equal( "KEY", lease.LicenseKey );
        Assert.Equal( new DateTime( 2026, 6, 1, 8, 0, 0, DateTimeKind.Utc ), lease.StartTime );
    }

    /// <summary>
    /// Tests that a server which lays its answer out over several lines still licenses the customer. How the answer
    /// is laid out is the choice of a deployment we do not control.
    /// </summary>
    [Fact]
    public void LineBreaksAreTolerated()
    {
        var lease = Parse( "License: KEY;\r\nEndTime: 2026-06-03T12:00:00Z\r\n" );

        Assert.Equal( "KEY", lease.LicenseKey );
        Assert.Equal( new DateTime( 2026, 6, 3, 12, 0, 0, DateTimeKind.Utc ), lease.EndTime );
    }

    /// <summary>
    /// Tests that a server which states something twice gets a defined answer rather than an incidental one. Two
    /// installations of the product must agree about the licence a customer holds.
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
    /// Tests the answer that would make the default end of a lease fall outside what a date can hold: a start at the
    /// end of time, and no end. It is refused like any other unusable answer. A server can say anything, and a
    /// <c>Try</c> method answers false rather than raising, whatever it is told.
    /// </summary>
    [Fact]
    public void StartAtTheEndOfTimeWithoutAnEndFails()
    {
        Assert.False( LicenseLease.TryDeserialize( "License: KEY; StartTime: 9999-12-31T23:59:59.9999999Z", _now, out var lease ) );
        Assert.Null( lease );
    }

    /// <summary>
    /// Tests that the same answer with an end of its own is read, so that the refusal above is about the arithmetic
    /// that cannot be done and not about the date itself.
    /// </summary>
    [Fact]
    public void StartAtTheEndOfTimeWithAnEndParses()
    {
        var lease = Parse( "License: KEY; StartTime: 9999-12-31T23:59:59.9999999Z; EndTime: 9999-12-31T23:59:59.9999999Z" );

        Assert.Equal( DateTime.MaxValue, lease.EndTime );
    }

    /// <summary>
    /// Tests that a licence key is taken whole, however long it is. A key cut short is a key that does not verify,
    /// and the customer would be told the licence they bought is invalid.
    /// </summary>
    [Fact]
    public void LongLicenseKeyParses()
    {
        var longKey = new string( 'A', 4096 );
        var lease = Parse( "License: " + longKey );

        Assert.Equal( longKey, lease.LicenseKey );
    }

    /// <summary>
    /// Tests that a renewal which grants the same period as before is recognized as such, so that the product
    /// leaves the configuration of the user untouched instead of rewriting it on every build.
    /// </summary>
    [Fact]
    public void EqualLeasesCompareEqual()
    {
        const string body = "License: KEY; StartTime: 2026-06-01T08:00:00Z; EndTime: 2026-06-04T08:00:00Z; RenewTime: 2026-06-03T08:00:00Z";

        Assert.Equal( Parse( body ), Parse( body ) );
    }
}
