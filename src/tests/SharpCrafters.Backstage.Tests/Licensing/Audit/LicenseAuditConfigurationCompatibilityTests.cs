// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Licensing.Audit;
using SharpCrafters.Backstage.Tests.Serialization;
using System;
using System.Collections.Immutable;
using Xunit;
using Xunit.Abstractions;

namespace SharpCrafters.Backstage.Tests.Licensing.Audit;

/// <summary>
/// Tests that <c>audit.json</c> keeps the shape that every released version reads.
/// </summary>
/// <remarks>
/// <para>
/// The file is read by every version of the product installed on the machine, and the versions released before an
/// audit could be identified by anything but a number read the keys of <c>LastAuditTimes</c> as numbers. A file this
/// version writes has to stay readable by them, which is why the numbers stay where they were and everything else
/// goes to a member those versions ignore.
/// </para>
/// <para>
/// Which of the two an identity belongs to is stated by <see cref="LicenseAuditKey"/> and never worked out from how
/// the identity looks. The two members are therefore named after the type of their keys, and the names they are
/// written under are pinned to the ones already in the file.
/// </para>
/// </remarks>
public sealed class LicenseAuditConfigurationCompatibilityTests : JsonSerializationTestsBase
{
    public LicenseAuditConfigurationCompatibilityTests( ITestOutputHelper logger ) : base( logger ) { }

    private static readonly DateTime _auditTime = new( 2025, 1, 15, 10, 0, 0, DateTimeKind.Utc );

    private LicenseAuditConfiguration Deserialize( string json )
    {
        Assert.True( this.JsonService.TryDeserialize( json, typeof(LicenseAuditConfiguration), out var result ) );

        return (LicenseAuditConfiguration) result;
    }

    private LicenseAuditConfiguration Roundtrip( LicenseAuditConfiguration configuration )
        => this.Deserialize( this.JsonService.Serialize( configuration, typeof(LicenseAuditConfiguration) ) );

    private string Serialize( LicenseAuditConfiguration configuration )
        => this.JsonService.Serialize( configuration, typeof(LicenseAuditConfiguration) );

    /// <summary>
    /// An identity given as a number is stored under that number, which is what an earlier version reads.
    /// </summary>
    [Fact]
    public void ANumberIsStoredAsANumber()
    {
        var configuration = new LicenseAuditConfiguration().SetLastAuditTime( LicenseAuditKey.FromNumber( 12345 ), _auditTime );

        Assert.Equal( _auditTime, configuration.LastAuditTimesByLong[12345] );
        Assert.Null( configuration.LastAuditTimesByString );
    }

    /// <summary>
    /// A file of nothing but numbers is written exactly as it always was: under the old member name, and with no
    /// second member beside it.
    /// </summary>
    [Fact]
    public void AFileOfNumbersIsUnchanged()
    {
        var json = this.Serialize( new LicenseAuditConfiguration().SetLastAuditTime( LicenseAuditKey.FromNumber( 12345 ), _auditTime ) );

        Assert.Contains( "\"LastAuditTimes\"", json, StringComparison.Ordinal );
        Assert.Contains( "\"12345\"", json, StringComparison.Ordinal );
        Assert.DoesNotContain( "LastAuditTimesByKey", json, StringComparison.Ordinal );

        // The members were renamed after the type of their keys; the file was not.
        Assert.DoesNotContain( "LastAuditTimesByLong", json, StringComparison.Ordinal );
        Assert.DoesNotContain( "LastAuditTimesByString", json, StringComparison.Ordinal );
    }

    /// <summary>
    /// A file written by an earlier version is read.
    /// </summary>
    [Fact]
    public void AFileOfAnEarlierVersionIsRead()
    {
        var configuration = this.Deserialize(
            """
            {
              "LastAuditTimes": {
                "12345": "2025-01-15T10:00:00Z"
              }
            }
            """ );

        Assert.True( configuration.TryGetLastAuditTime( LicenseAuditKey.FromNumber( 12345 ), out var lastAuditTime ) );
        Assert.Equal( _auditTime, lastAuditTime.ToUniversalTime() );
    }

    /// <summary>
    /// An identity given as text goes to the member beside, whatever it looks like.
    /// </summary>
    [Theory]
    [InlineData( "063454dd-f597-4dd0-a943-ff78a78090c7" )]
    [InlineData( "22" )]
    [InlineData( "-987654321" )]
    [InlineData( "" )]
    public void TextGoesBesideTheNumbers( string identity )
    {
        var configuration = new LicenseAuditConfiguration().SetLastAuditTime( LicenseAuditKey.FromText( identity ), _auditTime );

        Assert.Empty( configuration.LastAuditTimesByLong );
        Assert.Equal( _auditTime, configuration.LastAuditTimesByString![identity] );

        var json = this.Serialize( configuration );
        Assert.Contains( "LastAuditTimesByKey", json, StringComparison.Ordinal );
    }

    /// <summary>
    /// An identity that is a number and one that is the text of the same number are two different identities, and one
    /// does not answer for the other.
    /// </summary>
    /// <remarks>
    /// This is the case the previous design got wrong. PostSharp identifies a license by its number when the license
    /// key carries no globally unique identifier, so <c>22</c> is a license; the numbers of the other record are
    /// hashes of report content. Reading one as the other would throttle the audit of a license because a report that
    /// has nothing to do with it happened to hash to the same number.
    /// </remarks>
    [Fact]
    public void ANumberAndTheTextOfThatNumberAreNotTheSameIdentity()
    {
        var configuration = new LicenseAuditConfiguration()
            .SetLastAuditTime( LicenseAuditKey.FromNumber( 22 ), _auditTime )
            .SetLastAuditTime( LicenseAuditKey.FromText( "22" ), _auditTime.AddHours( 1 ) );

        Assert.Equal( _auditTime, configuration.LastAuditTimesByLong[22] );
        Assert.Equal( _auditTime.AddHours( 1 ), configuration.LastAuditTimesByString!["22"] );

        Assert.True( configuration.TryGetLastAuditTime( LicenseAuditKey.FromNumber( 22 ), out var number ) );
        Assert.Equal( _auditTime, number );

        Assert.True( configuration.TryGetLastAuditTime( LicenseAuditKey.FromText( "22" ), out var text ) );
        Assert.Equal( _auditTime.AddHours( 1 ), text );

        Assert.NotEqual( LicenseAuditKey.FromNumber( 22 ), LicenseAuditKey.FromText( "22" ) );
    }

    /// <summary>
    /// Both records survive a round trip, and each identity is found again as itself.
    /// </summary>
    [Fact]
    public void BothRecordsSurviveARoundTrip()
    {
        const string guid = "063454dd-f597-4dd0-a943-ff78a78090c7";

        var configuration = this.Roundtrip(
            new LicenseAuditConfiguration()
                .SetLastAuditTime( LicenseAuditKey.FromNumber( 12345 ), _auditTime )
                .SetLastAuditTime( LicenseAuditKey.FromText( guid ), _auditTime.AddHours( 1 ) ) );

        Assert.True( configuration.TryGetLastAuditTime( LicenseAuditKey.FromNumber( 12345 ), out var numberTime ) );
        Assert.Equal( _auditTime, numberTime.ToUniversalTime() );

        Assert.True( configuration.TryGetLastAuditTime( LicenseAuditKey.FromText( guid ), out var textTime ) );
        Assert.Equal( _auditTime.AddHours( 1 ), textTime.ToUniversalTime() );
    }

    /// <summary>
    /// A negative number is a number. The identity that the default provider gives is a hash rendered as a
    /// <see cref="long"/>, and about half of those are negative.
    /// </summary>
    [Theory]
    [InlineData( -123L )]
    [InlineData( long.MinValue )]
    [InlineData( long.MaxValue )]
    [InlineData( 0L )]
    public void ANegativeNumberIsANumber( long identity )
    {
        var configuration = this.Roundtrip( new LicenseAuditConfiguration().SetLastAuditTime( LicenseAuditKey.FromNumber( identity ), _auditTime ) );

        Assert.Null( configuration.LastAuditTimesByString );
        Assert.Equal( _auditTime, Assert.Single( configuration.LastAuditTimesByLong ).Value.ToUniversalTime() );
        Assert.True( configuration.TryGetLastAuditTime( LicenseAuditKey.FromNumber( identity ), out _ ) );
    }

    /// <summary>
    /// Nothing has been audited in an empty record, whichever kind of identity is asked for.
    /// </summary>
    [Fact]
    public void AnEmptyRecordHasAuditedNothing()
    {
        Assert.False( new LicenseAuditConfiguration().TryGetLastAuditTime( LicenseAuditKey.FromNumber( 12345 ), out _ ) );
        Assert.False( new LicenseAuditConfiguration().TryGetLastAuditTime( LicenseAuditKey.FromText( "anything" ), out _ ) );
    }

    /// <summary>
    /// An identity that identifies nothing is refused rather than recorded somewhere arbitrary, which would throttle
    /// the audit of everything else that could not be identified either.
    /// </summary>
    [Fact]
    public void AnIdentityOfNothingIsRefused()
    {
        Assert.False( new LicenseAuditConfiguration().TryGetLastAuditTime( default, out _ ) );
        Assert.Throws<ArgumentException>( () => new LicenseAuditConfiguration().SetLastAuditTime( default, _auditTime ) );
    }

    /// <summary>
    /// Auditing the same thing twice keeps the later moment, which is what makes the record a throttle.
    /// </summary>
    [Fact]
    public void AuditingAgainKeepsTheLaterMoment()
    {
        var configuration = new LicenseAuditConfiguration()
            .SetLastAuditTime( LicenseAuditKey.FromNumber( 12345 ), _auditTime )
            .SetLastAuditTime( LicenseAuditKey.FromNumber( 12345 ), _auditTime.AddDays( 2 ) );

        Assert.Equal( _auditTime.AddDays( 2 ), Assert.Single( configuration.LastAuditTimesByLong ).Value );
    }

    /// <summary>
    /// Both records are read from a file that carries both, under the names the file uses.
    /// </summary>
    [Fact]
    public void AFileCarryingBothRecordsIsRead()
    {
        var configuration = this.Roundtrip(
            new LicenseAuditConfiguration
            {
                LastAuditTimesByLong = ImmutableDictionary<long, DateTime>.Empty.Add( 1, _auditTime ),
                LastAuditTimesByString = ImmutableDictionary<string, DateTime>.Empty.Add( "x", _auditTime )
            } );

        Assert.Equal( _auditTime, configuration.LastAuditTimesByLong[1].ToUniversalTime() );
        Assert.Equal( _auditTime, configuration.LastAuditTimesByString!["x"].ToUniversalTime() );
    }
}
