// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Configuration;
using SharpCrafters.Backstage.Extensibility;
using SharpCrafters.Backstage.Serialization;
using SharpCrafters.Backstage.Telemetry;
using SharpCrafters.Backstage.Testing;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using Xunit;

namespace SharpCrafters.Backstage.Tests.ConfigurationManager;

public sealed class ConfigurationManagerTests : TestsBase
{
    public ConfigurationManagerTests( ITestOutputHelper logger ) : base(
        logger,
        applicationInfo: new TestApplicationInfo() { IsLongRunningProcess = true } )
    {
        this.InitializationOptions = this.InitializationOptions with
        {
            AdditionalJsonTypeInfoResolvers = new IJsonTypeInfoResolver[] { TestConfigurationJsonContext.Default }
        };
    }

    [Fact]
    public void InvalidJson()
    {
        var configurationManager = new Configuration.ConfigurationManager( this.ServiceProvider );
        var fileName = configurationManager.GetFilePath<TestConfigurationFile>();
        this.FileSystem.WriteAllText( fileName, "not valid json" );

        // Reading the file should be successful.
        var configuration = configurationManager.Get<TestConfigurationFile>();
        Assert.NotNull( configuration.Timestamp );
        Assert.Contains( this.Log.Entries, e => e.Severity == TestLoggerFactory.Severity.Error );

        // Updating the file should be successful.
        Assert.True( configurationManager.UpdateIf<TestConfigurationFile>( c => !c.IsModified, c => c with { IsModified = true } ) );
    }

    [Fact]
    public void VersionIsUpdated()
    {
        var configurationManager = new Configuration.ConfigurationManager( this.ServiceProvider );

        var initialConfiguration = configurationManager.Get<TestConfigurationFile>();
        Assert.Null( initialConfiguration.Timestamp );
        Assert.Null( initialConfiguration.Version );

        configurationManager.Update<TestConfigurationFile>( c => c with { IsModified = true } );

        var modifiedConfiguration = configurationManager.Get<TestConfigurationFile>();
        Assert.NotNull( modifiedConfiguration.Timestamp );
        Assert.Equal( 1, modifiedConfiguration.Version );
    }

    [Fact]
    public void OutsideModification()
    {
        var configurationManager = new Configuration.ConfigurationManager( this.ServiceProvider );
        var gotEvent = new ManualResetEvent( false );

        // Make sure we retrieve the value first.
        _ = configurationManager.Get<TestConfigurationFile>();

        configurationManager.ConfigurationFileChanged += file =>
        {
            if ( file is TestConfigurationFile )
            {
                gotEvent.Set();
            }
        };

        var path = configurationManager.GetFilePath<TestConfigurationFile>();
        var newValue = new TestConfigurationFile() { IsModified = true };
        var jsonService = this.ServiceProvider.GetRequiredBackstageService<IJsonSerializationService>();
        this.FileSystem.WriteAllText( path, jsonService.Serialize( newValue, typeof(TestConfigurationFile) ) );

        Assert.True( gotEvent.WaitOne( 30000 ) );

        var newValueFromManager = configurationManager.Get<TestConfigurationFile>();

        Assert.True( newValueFromManager.IsModified );
    }

    /// <summary>
    /// Earlier versions of Metalama keep the time of the last usage report of each project in a <c>Sessions</c> member
    /// of <c>telemetry.json</c>. This version no longer declares that member, and it must keep it through a read, an
    /// update and a second read, entries older than one day included: pruning them is the business of the version that
    /// owns them. See issue 2092.
    /// </summary>
    [Fact]
    public void TheSessionsOfAnEarlierVersionSurviveAnUpdateOfTheTelemetryConfiguration()
    {
        const string sessions = """
                                {
                                  "Project1": "2026-01-15T10:00:00Z",
                                  "Project2": "2020-01-01T00:00:00Z"
                                }
                                """;

        using var configurationManager = new Configuration.ConfigurationManager( this.ServiceProvider );
        var path = configurationManager.GetFilePath<TelemetryConfiguration>();

        this.FileSystem.WriteAllText( path, $$"""{ "UsageReportingAction": 1, "Sessions": {{sessions}} }""" );

        Assert.Equal( TelemetryConsent.Yes, configurationManager.Get<TelemetryConfiguration>().UsageConsent );

        configurationManager.Update<TelemetryConfiguration>( c => c with { UsageConsent = TelemetryConsent.No } );

        using var expected = JsonDocument.Parse( sessions );
        using var written = JsonDocument.Parse( this.FileSystem.ReadAllText( path ) );

        Assert.Equal( TelemetryConsent.No, (TelemetryConsent) written.RootElement.GetProperty( "UsageReportingAction" ).GetInt32() );
        Assert.Equal( JsonSerializer.Serialize( expected.RootElement ), JsonSerializer.Serialize( written.RootElement.GetProperty( "Sessions" ) ) );

        // A second read and update, from a manager that has not cached anything, keeps it as well.
        using var otherConfigurationManager = new Configuration.ConfigurationManager( this.ServiceProvider );
        otherConfigurationManager.Update<TelemetryConfiguration>( c => c with { MatomoSalt = 1 } );

        using var rewritten = JsonDocument.Parse( this.FileSystem.ReadAllText( path ) );

        Assert.Equal( JsonSerializer.Serialize( expected.RootElement ), JsonSerializer.Serialize( rewritten.RootElement.GetProperty( "Sessions" ) ) );
    }
}