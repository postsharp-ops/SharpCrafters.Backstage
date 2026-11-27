// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Telemetry;
using SharpCrafters.Backstage.Testing;
using System;
using System.IO;
using Xunit;

namespace SharpCrafters.Backstage.Tests.Telemetry;

/// <summary>
/// Tests that <see cref="TestTelemetryEnvironment"/> records what the real telemetry context sends, so that the tests of the
/// products that use it prove what they claim.
/// </summary>
public sealed class TestTelemetryEnvironmentTests
{
    private static readonly string _repositoryRoot = Path.Combine( Path.GetTempPath(), "repo" );
    private static readonly string _projectDirectory = Path.Combine( _repositoryRoot, "src", "project" );

    /// <summary>
    /// Checks that a repository opt-out prevents the usage session and the report for upload, and that the local crash report
    /// is still written.
    /// </summary>
    [Fact]
    public void RepositoryOptOut_RecordsOnlyLocalCrashReport()
    {
        var environment = new TestTelemetryEnvironment();
        environment.CreateRepository( _repositoryRoot, telemetryEnabled: false );

        var context = environment.TelemetryService.OpenContext( environment.TelemetryService.GetPolicy( _projectDirectory ) );

        context.StartUsageSession( "TestUsage", "Project" ).Dispose();
        context.ReportException( new InvalidOperationException() );

        Assert.Empty( environment.UsageSessionFactory.Sessions );
        Assert.Empty( environment.ExceptionCapturer.CapturedReports );
        Assert.Single( environment.GetLocalCrashReports() );
    }

    /// <summary>
    /// Checks that no local crash report is written when the caller wrote its own, whatever the policy is.
    /// </summary>
    [Theory]
    [InlineData( false )]
    [InlineData( null )]
    public void WriteLocalReportFalse_WritesNoLocalCrashReport( bool? telemetryEnabled )
    {
        var environment = new TestTelemetryEnvironment();
        environment.CreateRepository( _repositoryRoot, telemetryEnabled );

        var context = environment.TelemetryService.OpenContext( environment.TelemetryService.GetPolicy( _projectDirectory ) );

        context.ReportException( new InvalidOperationException(), writeLocalReport: false );

        Assert.Empty( environment.GetLocalCrashReports() );
    }

    /// <summary>
    /// Checks that a repository without opt-out records the usage session, the report for upload and the local crash report.
    /// </summary>
    [Fact]
    public void RepositoryWithoutOptOut_RecordsSessionAndException()
    {
        var environment = new TestTelemetryEnvironment();
        environment.CreateRepository( _repositoryRoot, telemetryEnabled: null );

        var context = environment.TelemetryService.OpenContext( environment.TelemetryService.GetPolicy( _projectDirectory ) );
        var exception = new InvalidOperationException();

        context.StartUsageSession( "TestUsage", "Project" ).Dispose();
        context.ReportException( exception );

        var session = Assert.Single( environment.UsageSessionFactory.Sessions );
        Assert.Equal( "TestUsage", session.Kind );

        var report = Assert.Single( environment.ExceptionCapturer.CapturedReports );
        Assert.Same( exception, report.Exception );
        Assert.Equal( ExceptionReportingKind.Exception, report.Kind );
        Assert.Single( environment.GetLocalCrashReports() );
    }
}
