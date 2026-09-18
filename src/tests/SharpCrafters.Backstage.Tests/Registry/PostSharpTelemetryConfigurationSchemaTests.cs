// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using PostSharp.Backstage.Configuration;
using SharpCrafters.Backstage.Configuration;
using SharpCrafters.Backstage.Configuration.Registry;
using SharpCrafters.Backstage.Extensibility;
using SharpCrafters.Backstage.Telemetry;
using SharpCrafters.Backstage.Testing;
using System;
using System.Collections.Immutable;
using Xunit;
using Xunit.Abstractions;

namespace SharpCrafters.Backstage.Tests.Registry;

/// <summary>
/// Tests that the telemetry consents of PostSharp live where PostSharp 2026.0 puts them, so that a user who answers
/// the privacy question in either version is not asked again by the other.
/// </summary>
public sealed class PostSharpTelemetryConfigurationSchemaTests : TestsBase
{
    private const string _feedbackKeyPath = @"Software\SharpCrafters\PostSharp 3\Feedback";

    private readonly TestRegistryService _registry = new();

    public PostSharpTelemetryConfigurationSchemaTests( ITestOutputHelper logger ) : base( logger ) { }

    protected override void ConfigureServices( ServiceProviderBuilder services )
    {
        base.ConfigureServices( services );
        services.AddService( typeof(IRegistryService), this._registry );
    }

    private RegistryConfigurationManager CreateManager()
        => new( this.ServiceProvider, new InMemoryConfigurationManager( this.ServiceProvider ), [new PostSharpTelemetryConfigurationSchema()] );

    private IRegistryKey FeedbackKey() => this._registry.GetOrCreateKey( RegistryHiveKind.CurrentUser, _feedbackKeyPath );

    private TelemetryConfiguration Read()
    {
        using var manager = this.CreateManager();

        return manager.Get<TelemetryConfiguration>();
    }

    private void Update( Func<TelemetryConfiguration, TelemetryConfiguration> transform )
    {
        using var manager = this.CreateManager();
        manager.Update( typeof(TelemetryConfiguration), current => transform( (TelemetryConfiguration) current ) );
    }

    [Fact]
    public void TheTelemetrySettingsLiveUnderTheFeedbackKey()
    {
        using var manager = this.CreateManager();

        Assert.Equal(
            @"HKEY_CURRENT_USER\Software\SharpCrafters\PostSharp 3\Feedback",
            manager.GetStore( typeof(TelemetryConfiguration) ).Path );
    }

    /// <summary>
    /// The consent of PostSharp 2026.0 and the consent of this version are the same three numbers, so a value
    /// written by either is read by the other with the meaning it was given. Ask and Default are both zero.
    /// </summary>
    [Theory]
    [InlineData( 0, TelemetryConsent.Default )]
    [InlineData( 1, TelemetryConsent.Yes )]
    [InlineData( 2, TelemetryConsent.No )]
    public void TheConsentsOfBothVersionsAreTheSameNumbers( int storedValue, TelemetryConsent expected )
    {
        var key = this.FeedbackKey();
        key.SetDWordValue( "UsageReportingAction", storedValue );
        key.SetDWordValue( "NewExceptionReportingAction", storedValue );
        key.SetDWordValue( "NewPerformanceProblemReportingAction", storedValue );

        var configuration = this.Read();

        Assert.Equal( expected, configuration.UsageConsent );
        Assert.Equal( expected, configuration.ExceptionConsent );
        Assert.Equal( expected, configuration.PerformanceProblemConsent );
    }

    /// <summary>
    /// A consent chosen here is written where the privacy panel of the other version reads it, as a number and not
    /// as a name: that version casts the value, so a string would make it fall back to its default.
    /// </summary>
    [Fact]
    public void AConsentIsWrittenWhereTheOtherVersionReadsIt()
    {
        this.Update( c => c with { UsageConsent = TelemetryConsent.No, ExceptionConsent = TelemetryConsent.Yes } );

        var key = this.FeedbackKey();

        Assert.Equal( 2, key.GetValue( "UsageReportingAction" ) );
        Assert.IsType<int>( key.GetValue( "UsageReportingAction" ) );
        Assert.Equal( 1, key.GetValue( "NewExceptionReportingAction" ) );
    }

    /// <summary>
    /// The device identifier is a GUID in its textual form, which is how the other version stores it.
    /// </summary>
    [Fact]
    public void TheDeviceIdentifierIsSharedAsText()
    {
        var deviceId = Guid.NewGuid();
        this.FeedbackKey().SetStringValue( "DeviceId", deviceId.ToString() );

        Assert.Equal( deviceId, this.Read().DeviceId );

        var otherDeviceId = Guid.NewGuid();
        this.Update( c => c with { DeviceId = otherDeviceId } );

        Assert.Equal( otherDeviceId.ToString(), this.FeedbackKey().GetValue( "DeviceId" ) );
    }

    /// <summary>
    /// The moment at which the identifier was generated is what the monthly rotation is counted from, and the other
    /// version counts from the same value.
    /// </summary>
    [Fact]
    public void TheRotationIsCountedFromTheSameMoment()
    {
        var moment = new DateTime( 2026, 9, 7, 10, 0, 0, DateTimeKind.Utc );
        this.FeedbackKey().SetQWordValue( "DeviceIdTimestamp", RegistryValueCodec.DateTimeToQWord( moment ) );

        Assert.Equal( moment, this.Read().LastSaltChangeTime!.Value.ToUniversalTime() );
    }

    /// <summary>
    /// A setting that the other version does not have goes into the same key under a name of this version, which
    /// the other version ignores.
    /// </summary>
    [Fact]
    public void ASettingOfThisVersionGoesIntoTheSameKey()
    {
        this.Update( c => c with { MatomoSalt = 1234, RetentionPeriodInDays = 7 } );

        var key = this.FeedbackKey();
        Assert.Equal( 1234L, key.GetValue( "MatomoSalt" ) );
        Assert.Equal( 7, key.GetValue( "RetentionPeriodInDays" ) );

        var configuration = this.Read();
        Assert.Equal( 1234, configuration.MatomoSalt );
        Assert.Equal( 7, configuration.RetentionPeriodInDays );
    }

    /// <summary>
    /// The settings that the other version keeps in the same key are not ours to delete.
    /// </summary>
    [Fact]
    public void TheSettingsOfTheOtherVersionSurviveAnUpdate()
    {
        var key = this.FeedbackKey();
        key.SetStringValue( "Email", "a-setting-we-no-longer-have" );
        key.SetDWordValue( "ErrorReportingAction", 1 );
        key.SetDWordValue( "BuildCount", 12 );

        this.Update( c => c with { UsageConsent = TelemetryConsent.Yes } );

        Assert.Equal( "a-setting-we-no-longer-have", key.GetValue( "Email" ) );
        Assert.Equal( 1, key.GetValue( "ErrorReportingAction" ) );
        Assert.Equal( 12, key.GetValue( "BuildCount" ) );
    }

    /// <summary>
    /// The decisions taken about the issues are a sub-key with one value per issue, so that each is legible on its
    /// own in the registry editor rather than being buried in a serialized blob.
    /// </summary>
    [Fact]
    public void TheDecisionsAboutIssuesAreOneValueEach()
    {
        this.Update(
            c => c with
            {
                Issues = ImmutableDictionary<string, ReportingStatus>.Empty
                    .Add( "hash-of-a-reported-issue", ReportingStatus.Reported )
                    .Add( "hash-of-an-ignored-issue", ReportingStatus.Ignored )
            } );

        using var issuesKey = this.FeedbackKey().OpenSubKey( "Issues" );

        Assert.NotNull( issuesKey );
        Assert.Equal( (int) ReportingStatus.Reported, issuesKey.GetValue( "hash-of-a-reported-issue" ) );
        Assert.Equal( (int) ReportingStatus.Ignored, issuesKey.GetValue( "hash-of-an-ignored-issue" ) );

        var configuration = this.Read();
        Assert.Equal( ReportingStatus.Reported, configuration.Issues["hash-of-a-reported-issue"] );
        Assert.Equal( ReportingStatus.Ignored, configuration.Issues["hash-of-an-ignored-issue"] );
    }

    /// <summary>
    /// An entry that the object no longer holds is removed. These dictionaries are pruned as they are written, and a
    /// value left behind would grow the key without bound.
    /// </summary>
    [Fact]
    public void AnEntryThatIsPrunedIsRemoved()
    {
        var sessions = ImmutableDictionary<string, DateTime>.Empty
            .Add( "old-session", new DateTime( 2026, 1, 1, 0, 0, 0, DateTimeKind.Utc ) )
            .Add( "new-session", new DateTime( 2026, 9, 1, 0, 0, 0, DateTimeKind.Utc ) );

        this.Update( c => c with { Sessions = sessions } );

        using ( var sessionsKey = this.FeedbackKey().OpenSubKey( "Sessions" ) )
        {
            Assert.Equal( 2, sessionsKey!.GetValueNames().Count );
        }

        this.Update( c => c with { Sessions = sessions.Remove( "old-session" ) } );

        using ( var sessionsKey = this.FeedbackKey().OpenSubKey( "Sessions" ) )
        {
            Assert.Equal( ["new-session"], sessionsKey!.GetValueNames() );
        }

        Assert.False( this.Read().Sessions.ContainsKey( "old-session" ) );
    }

    /// <summary>
    /// The names of these entries are hashes and session identifiers, written in one case and read in another often
    /// enough to matter, and the registry compares names without regard to case in any event.
    /// </summary>
    [Fact]
    public void TheEntriesAreFoundWhateverTheirCase()
    {
        this.Update( c => c with { Issues = ImmutableDictionary<string, ReportingStatus>.Empty.Add( "ABCDEF", ReportingStatus.Reported ) } );

        Assert.Equal( ReportingStatus.Reported, this.Read().Issues["abcdef"] );
    }
}
