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
/// The file is read by every version of the product installed on the machine, and the versions released before an
/// audit could be identified by anything but a number read the keys of <c>LastAuditTimes</c> as numbers. A file this
/// version writes has to stay readable by them, which is why the numeric identities stay where they were and only
/// the others go to a member those versions ignore.
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

    /// <summary>
    /// A licence identified by a number is stored under that number, which is what an earlier version reads.
    /// </summary>
    [Fact]
    public void ANumericIdentityIsStoredAsANumber()
    {
        var configuration = new LicenseAuditConfiguration().SetLastAuditTime( "12345", _auditTime );

        Assert.Equal( _auditTime, configuration.LastAuditTimes[12345] );
        Assert.Null( configuration.LastAuditTimesByKey );
    }

    /// <summary>
    /// A file written by this version carries the numeric identities exactly where an earlier version looks for them,
    /// and carries no auxiliary member at all when there is nothing to put in it.
    /// </summary>
    [Fact]
    public void AFileOfNumericIdentitiesIsUnchanged()
    {
        var json = this.JsonService.Serialize(
            new LicenseAuditConfiguration().SetLastAuditTime( "12345", _auditTime ),
            typeof(LicenseAuditConfiguration) );

        Assert.Contains( "\"LastAuditTimes\"", json, StringComparison.Ordinal );
        Assert.Contains( "\"12345\"", json, StringComparison.Ordinal );
        Assert.DoesNotContain( "LastAuditTimesByKey", json, StringComparison.Ordinal );
    }

    /// <summary>
    /// A file written by an earlier version is read by this one, which is the direction that matters on a machine
    /// where the product is being upgraded.
    /// </summary>
    [Fact]
    public void AFileOfAnEarlierVersionIsRead()
    {
        const string json = """
                            {
                              "LastAuditTimes": {
                                "12345": "2025-01-15T10:00:00Z"
                              },
                              "LastMatomoAuditTime": "2025-01-14T08:00:00Z",
                              "version": 1
                            }
                            """;

        var configuration = this.Deserialize( json );

        Assert.True( configuration.TryGetLastAuditTime( "12345", out var lastAuditTime ) );
        Assert.Equal( _auditTime, lastAuditTime.ToUniversalTime() );
    }

    /// <summary>
    /// An identity that is not a number goes to the auxiliary member, so that the member an earlier version reads
    /// holds only what that version can parse.
    /// </summary>
    [Fact]
    public void AnIdentityThatIsNotANumberGoesBeside()
    {
        const string guid = "063454dd-f597-4dd0-a943-ff78a78090c7";

        var configuration = new LicenseAuditConfiguration().SetLastAuditTime( guid, _auditTime );

        Assert.Empty( configuration.LastAuditTimes );
        Assert.Equal( _auditTime, configuration.LastAuditTimesByKey![guid] );

        var json = this.JsonService.Serialize( configuration, typeof(LicenseAuditConfiguration) );

        Assert.Contains( "LastAuditTimesByKey", json, StringComparison.Ordinal );
    }

    /// <summary>
    /// Both kinds of identity survive the round trip and are found again by the identity they were stored under.
    /// </summary>
    [Fact]
    public void BothKindsOfIdentitySurviveTheRoundTrip()
    {
        const string guid = "063454dd-f597-4dd0-a943-ff78a78090c7";

        var configuration = this.Roundtrip(
            new LicenseAuditConfiguration()
                .SetLastAuditTime( "12345", _auditTime )
                .SetLastAuditTime( guid, _auditTime.AddHours( 1 ) ) );

        Assert.True( configuration.TryGetLastAuditTime( "12345", out var numericTime ) );
        Assert.Equal( _auditTime, numericTime.ToUniversalTime() );

        Assert.True( configuration.TryGetLastAuditTime( guid, out var guidTime ) );
        Assert.Equal( _auditTime.AddHours( 1 ), guidTime.ToUniversalTime() );
    }

    /// <summary>
    /// An identity that parses as a number but is not written back the same way is kept beside, so that it is found
    /// again under the identity it was stored under.
    /// </summary>
    [Theory]
    [InlineData( "0123" )]
    [InlineData( "+123" )]
    [InlineData( "-123" )]
    [InlineData( " 123" )]
    public void AnIdentityThatDoesNotRoundTripAsANumberGoesBeside( string auditKey )
    {
        var configuration = this.Roundtrip( new LicenseAuditConfiguration().SetLastAuditTime( auditKey, _auditTime ) );

        Assert.Empty( configuration.LastAuditTimes );
        Assert.True( configuration.TryGetLastAuditTime( auditKey, out var lastAuditTime ) );
        Assert.Equal( _auditTime, lastAuditTime.ToUniversalTime() );
    }

    /// <summary>
    /// Nothing has been audited in a configuration that carries neither member.
    /// </summary>
    [Fact]
    public void AnEmptyConfigurationHasAuditedNothing()
    {
        Assert.False( new LicenseAuditConfiguration().TryGetLastAuditTime( "12345", out _ ) );
        Assert.False( new LicenseAuditConfiguration().TryGetLastAuditTime( "anything", out _ ) );
    }

    /// <summary>
    /// Recording an audit again replaces the moment rather than adding a second entry.
    /// </summary>
    [Fact]
    public void AuditingAgainReplacesTheMoment()
    {
        var configuration = new LicenseAuditConfiguration()
            .SetLastAuditTime( "12345", _auditTime )
            .SetLastAuditTime( "12345", _auditTime.AddDays( 2 ) );

        Assert.Equal( _auditTime.AddDays( 2 ), Assert.Single( configuration.LastAuditTimes ).Value );
    }

    /// <summary>
    /// An auxiliary member written by a later version survives a rewrite by this one, as every member of a
    /// configuration object does.
    /// </summary>
    [Fact]
    public void TheDictionariesAreIndependent()
    {
        var configuration = new LicenseAuditConfiguration
        {
            LastAuditTimes = ImmutableDictionary<long, DateTime>.Empty.Add( 1, _auditTime ),
            LastAuditTimesByKey = ImmutableDictionary<string, DateTime>.Empty.Add( "x", _auditTime )
        };

        configuration = this.Roundtrip( configuration );

        Assert.Equal( _auditTime, configuration.LastAuditTimes[1] );
        Assert.Equal( _auditTime, configuration.LastAuditTimesByKey!["x"] );
    }
}
