// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using PostSharp.Backstage.Configuration;
using SharpCrafters.Backstage.Configuration;
using SharpCrafters.Backstage.Configuration.Registry;
using SharpCrafters.Backstage.Extensibility;
using SharpCrafters.Backstage.Licensing;
using SharpCrafters.Backstage.Licensing.Audit;
using SharpCrafters.Backstage.Licensing.LicenseServer;
using SharpCrafters.Backstage.Serialization;
using SharpCrafters.Backstage.Telemetry;
using SharpCrafters.Backstage.Testing;
using System;
using System.Collections.Immutable;
using Xunit;
using Xunit.Abstractions;

namespace SharpCrafters.Backstage.Tests.Registry;

/// <summary>
/// Tests that every configuration object the registry holds survives being written and read back.
/// </summary>
/// <remarks>
/// <para>
/// The schemas are tested elsewhere one value at a time, which says that each value lands where PostSharp 2026.0
/// looks for it. This says something the per-value tests cannot: that nothing is lost. A member the schema forgets to
/// write, or writes and cannot read, passes every test that only looks at the members the author remembered.
/// </para>
/// <para>
/// The comparison is of the JSON of the two objects rather than of the objects themselves, for two reasons. A
/// configuration object carries the moment its store was last written, which differs by construction between an
/// object built here and one read back, and record equality would compare it. And when an object does differ, a
/// difference between two pieces of JSON can be read, where a failed equality of two records cannot.
/// </para>
/// </remarks>
public sealed class RegistryConfigurationRoundTripTests : TestsBase
{
    private readonly TestRegistryService _registry = new();
    private readonly IJsonSerializationService _json = new JsonSerializationService( [BackstageJsonContext.Default] );

    public RegistryConfigurationRoundTripTests( ITestOutputHelper logger ) : base( logger ) { }

    protected override void ConfigureServices( ServiceProviderBuilder services )
    {
        base.ConfigureServices( services );
        services.AddService( typeof(IRegistryService), this._registry );
    }

    /// <summary>
    /// Creates a manager over the schemas that the product declares, rather than over a list written here, so that a
    /// schema added to the product is covered by these tests without anything being added to them.
    /// </summary>
    private RegistryConfigurationManager CreateManager()
        => new( this.ServiceProvider, new InMemoryConfigurationManager( this.ServiceProvider ), PostSharpConfigurationSchemas.Create( this.Time ) );

    /// <summary>
    /// Writes an object, reads it back through a manager that has never seen it, and compares the two.
    /// </summary>
    /// <remarks>
    /// The second manager is a new one because the first caches what it wrote: reading through it would compare the
    /// object with itself and say nothing about what reached the registry.
    /// </remarks>
    private void AssertRoundTrip<T>( T original )
        where T : ConfigurationFile
    {
        using ( var writer = this.CreateManager() )
        {
            // Either outcome leaves the store saying what the object says. An object with nothing in it, written to a
            // key that holds nothing, changes nothing and reports so.
            var outcome = writer.Update( typeof(T), _ => original );

            Assert.True(
                outcome is ConfigurationUpdateOutcome.Updated or ConfigurationUpdateOutcome.NoChange,
                $"The write reported {outcome}." );
        }

        T readBack;

        using ( var reader = this.CreateManager() )
        {
            readBack = reader.Get<T>();
        }

        // The version counts the writes made to the object, so the one read back is one ahead of the one written.
        // That is the difference the round trip is supposed to make; every other difference is a defect.
        Assert.Equal( this.ToJson( original with { Version = null } ), this.ToJson( readBack with { Version = null } ) );
    }

    private string ToJson<T>( T value )
        where T : ConfigurationFile
        => this._json.Serialize( value, typeof(T) );

    /// <summary>
    /// The licensing configuration, with a key in every slot it has: the legacy one, the list, and a group named
    /// after a version.
    /// </summary>
    [Fact]
    public void TheLicensingConfigurationSurvives()
        => this.AssertRoundTrip(
            new LicensingConfiguration
            {
                LegacyLicense = "a-legacy-key",
                Licenses = ImmutableArray.Create<string?>( "the-first-key", "the-second-key" ),
                LicensesByMinimalVersion = ImmutableDictionary<string, ImmutableArray<string?>>.Empty
                    .Add( "2027.0.0", ImmutableArray.Create<string?>( "a-key-of-a-later-version" ) ),
                LastEvaluationStartDate = new DateTime( 2026, 6, 24, 22, 0, 0, DateTimeKind.Utc ),
                AllowInsecureLicenseServer = true,
                CommunityLicenseReason = CommunityLicenseReason.OpenSource
            } );

    /// <summary>
    /// The licensing configuration with nothing in it, which is what a machine where nothing has been registered
    /// holds. An empty object is where a schema that writes a default instead of nothing shows itself.
    /// </summary>
    [Fact]
    public void AnEmptyLicensingConfigurationSurvives() => this.AssertRoundTrip( new LicensingConfiguration() );

    /// <summary>
    /// The telemetry configuration, including the three consents that PostSharp 2026.0 shares and the bookkeeping it
    /// does not have.
    /// </summary>
    [Fact]
    public void TheTelemetryConfigurationSurvives()
        => this.AssertRoundTrip(
            new TelemetryConfiguration
            {
                UsageConsent = TelemetryConsent.Yes,
                ExceptionConsent = TelemetryConsent.No,
                PerformanceProblemConsent = TelemetryConsent.Default,
                DeviceId = new Guid( "3607ebb5-fd96-473e-a6f4-fbfe79b8200e" ),
                LastSaltChangeTime = new DateTime( 2026, 8, 1, 10, 0, 0, DateTimeKind.Utc ),
                LastUploadTime = new DateTime( 2026, 9, 1, 11, 0, 0, DateTimeKind.Utc ),
                LastMatomoPostTime = new DateTime( 2026, 9, 2, 12, 0, 0, DateTimeKind.Utc ),
                MatomoSalt = 1234567890123,
                UsageTrackingSalt = -987654321,
                ExceptionReportingSalt = 42,
                LicenseAuditSalt = 0,
                RetentionPeriodInDays = 30,
                Issues = ImmutableDictionary<string, ReportingStatus>.Empty.Add( "an-issue-hash", ReportingStatus.Reported ),
                IssuePrompts = ImmutableDictionary<string, DateTime>.Empty
                    .Add( "another-issue-hash", new DateTime( 2026, 9, 3, 13, 0, 0, DateTimeKind.Utc ) ),
                Sessions = ImmutableDictionary<string, DateTime>.Empty
                    .Add( "a-session", new DateTime( 2026, 9, 4, 14, 0, 0, DateTimeKind.Utc ) )
            } );

    /// <summary>
    /// The telemetry configuration with nothing in it. The consents default to the value that means the question has
    /// not been asked, and that value has to survive as itself rather than becoming a yes or a no.
    /// </summary>
    [Fact]
    public void AnEmptyTelemetryConfigurationSurvives() => this.AssertRoundTrip( new TelemetryConfiguration() );

    /// <summary>
    /// The leases held from license servers, which are keyed by the address of the server.
    /// </summary>
    [Fact]
    public void TheLicenseServerConfigurationSurvives()
        => this.AssertRoundTrip(
            new LicenseServerConfiguration
            {
                Leases = ImmutableDictionary<string, LeaseConfiguration>.Empty
                    .Add(
                        "https!__licenses.example.com_",
                        new LeaseConfiguration
                        {
                            LicenseKey = "a-leased-key",
                            StartTime = new DateTime( 2026, 9, 1, 8, 0, 0, DateTimeKind.Utc ),
                            EndTime = new DateTime( 2026, 10, 1, 8, 0, 0, DateTimeKind.Utc ),
                            RenewTime = new DateTime( 2026, 9, 20, 8, 0, 0, DateTimeKind.Utc )
                        } )
            } );

    /// <summary>
    /// The record of the audits, with an identity of each kind: the number that the default provider gives, and the
    /// globally unique identifier that PostSharp gives a licence that has one.
    /// </summary>
    [Fact]
    public void TheLicenseAuditConfigurationSurvives()
        => this.AssertRoundTrip(
            new LicenseAuditConfiguration
                {
                    LastMatomoAuditTime = new DateTime( 2026, 9, 10, 9, 0, 0, DateTimeKind.Utc )
                }
                .SetLastAuditTime( LicenseAuditKey.FromText( "22" ), new DateTime( 2026, 9, 11, 9, 0, 0, DateTimeKind.Utc ) )
                .SetLastAuditTime( LicenseAuditKey.FromText( "-987654321" ), new DateTime( 2026, 9, 12, 9, 0, 0, DateTimeKind.Utc ) )
                .SetLastAuditTime( LicenseAuditKey.FromText( "063454dd-f597-4dd0-a943-ff78a78090c7" ), new DateTime( 2026, 9, 13, 9, 0, 0, DateTimeKind.Utc ) ) );

    /// <summary>
    /// Writing the same object twice leaves the store saying the same thing, and the second write reports that
    /// nothing changed rather than writing the same values again.
    /// </summary>
    [Fact]
    public void WritingTheSameObjectTwiceChangesNothing()
    {
        var configuration = new LicensingConfiguration
        {
            Licenses = ImmutableArray.Create<string?>( "a-key" ), LastEvaluationStartDate = new DateTime( 2026, 6, 24, 22, 0, 0, DateTimeKind.Utc )
        };

        using ( var writer = this.CreateManager() )
        {
            Assert.Equal( ConfigurationUpdateOutcome.Updated, writer.Update( typeof(LicensingConfiguration), _ => configuration ) );
        }

        using ( var rewriter = this.CreateManager() )
        {
            var stored = rewriter.Get<LicensingConfiguration>();

            // The same object, written by a manager that has just read it: the values are already what they should
            // be, so there is nothing to write. This is what keeps the shared keys from being rewritten on every run.
            Assert.Equal( ConfigurationUpdateOutcome.NoChange, rewriter.Update( typeof(LicensingConfiguration), _ => stored ) );
        }
    }

    /// <summary>
    /// A round trip through the registry does not disturb the values of PostSharp 2026.0 that this version has no
    /// member for.
    /// </summary>
    [Fact]
    public void ValuesOfTheOtherVersionSurviveARoundTrip()
    {
        var root = this._registry.GetOrCreateKey( RegistryHiveKind.CurrentUser, @"Software\SharpCrafters\PostSharp 3" );
        root.SetStringValue( "LatestStartedVsxVersion", "2026.1.5.0" );
        root.SetQWordValue( "LastPrepareToolTime", 842988638113 );

        this.AssertRoundTrip( new LicensingConfiguration { Licenses = ImmutableArray.Create<string?>( "a-key" ) } );

        Assert.Equal( "2026.1.5.0", root.GetValue( "LatestStartedVsxVersion" ) );
        Assert.Equal( 842988638113L, root.GetValue( "LastPrepareToolTime" ) );
    }
}
