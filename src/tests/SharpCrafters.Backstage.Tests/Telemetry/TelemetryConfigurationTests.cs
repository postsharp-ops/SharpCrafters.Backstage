// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using Metalama.Backstage;
using Metalama.Backstage.Metalama;
using Microsoft.Extensions.DependencyInjection;
using SharpCrafters.Backstage.Configuration;
using SharpCrafters.Backstage.Extensibility;
using SharpCrafters.Backstage.Telemetry;
using SharpCrafters.Backstage.Testing;
using SharpCrafters.Backstage.Utilities;
using System;
using Xunit;
using Xunit.Abstractions;

namespace SharpCrafters.Backstage.Tests.Telemetry;

public sealed class TelemetryConfigurationTests : TestsBase
{
    public TelemetryConfigurationTests( ITestOutputHelper logger ) : base( logger, new TestApplicationInfo() { IsTelemetryEnabled = true } ) { }

    [Fact]
    public void FirstRunDefaultsExceptionAndPerformanceChannelsToReviewFirst()
    {
        // #1674: Capture and notification are decoupled from sending. On first run, the exception/performance
        // channel must default to review-first (ReportingAction.Default) rather than auto-send (ReportingAction.Yes),
        // so the most sensitive telemetry (stack traces, paths, Exception.Data) stays under explicit user control
        // until the user clicks Report. Usage telemetry remains opt-out and is unchanged.
        this.TelemetryConfigurationService.EnsureActivated();

        var configuration = this.ConfigurationManager!.Get<TelemetryConfiguration>();

        Assert.Equal( TelemetryConsent.Default, configuration.ExceptionConsent );
        Assert.Equal( TelemetryConsent.Default, configuration.PerformanceProblemConsent );
    }

    [Theory]
    [InlineData( null, true )]
    [InlineData( "", true )]
    [InlineData( "true", false )]
    [InlineData( "anything", false )]
    [InlineData( "false", true )]
    [InlineData( "FALSE", true )]
    [InlineData( "False", true )]
    [InlineData( "0", true )]
    public void DisabledWithEnvironmentVariable( string? value, bool isEnabled )
    {
        if ( value != null )
        {
            this.EnvironmentVariableProvider.Environment[MetalamaProduct.Profile.GetEnvironmentVariableName( TelemetryConfiguration.OptOutEnvironmentVariable )] = value;
        }

        this.TelemetryConfigurationService.SetConsent( TelemetryConsent.Yes );
        Assert.Equal( isEnabled, this.TelemetryConfigurationService.GetEffectiveConsent( TelemetryScenario.Usage ) != TelemetryConsent.No );
    }

    [Fact]
    public void SaltRotation()
    {
        this.Time.Set( new DateTime( 2025, 4, 10, 0, 0, 0, DateTimeKind.Utc ) );
        this.TelemetryConfigurationService.EnsureActivated();
        var initialSalt = this.TelemetryConfigurationService.GetSalt( TelemetrySaltKind.Matomo );

        // There should be no change on April 30th or even on May the 4th because the first Monday is the 5th.
        this.Time.Set( new DateTime( 2025, 4, 30, 0, 0, 0, DateTimeKind.Utc ) );
        Assert.Equal( initialSalt, this.TelemetryConfigurationService.GetSalt( TelemetrySaltKind.Matomo ) );

        this.Time.Set( new DateTime( 2025, 5, 4, 0, 0, 0, DateTimeKind.Utc ) );
        Assert.Equal( initialSalt, this.TelemetryConfigurationService.GetSalt( TelemetrySaltKind.Matomo ) );

        // Now there should be a change.
        this.Time.Set( new DateTime( 2025, 5, 5, 0, 0, 0, DateTimeKind.Utc ) );
        Assert.NotEqual( initialSalt, this.TelemetryConfigurationService.GetSalt( TelemetrySaltKind.Matomo ) );
    }

    /// <summary>
    /// A salt that is several boundaries old is rotated once, on the next first Monday, and not on the first run
    /// after the gap.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A machine left unused comes back with a salt older than one boundary. We wait for this month's first Monday
    /// rather than rotating at once against the previous month's, so the rotation can be up to six days late. That
    /// costs nothing: the identifier it holds on to was not used during the gap either. Rotating at once would cost
    /// something, because the next first Monday is days away and would rotate it a second time, giving one
    /// identifier a lifetime of a few days in the middle of a week -- which is what rotating on a Monday exists to
    /// avoid.
    /// </para>
    /// <para>
    /// PostSharp answers this differently: <c>DeviceIdRotationPolicy.GetCurrentRotationDate</c> falls back to the
    /// previous month's Monday, so it rotates at once and again days later. The two products share the registry
    /// value recording the last rotation, so on a machine carrying both, whichever rotates first satisfies the
    /// other, and the difference is in how soon rather than in how often.
    /// </para>
    /// </remarks>
    [Fact]
    public void SaltRotatesOnceOnTheNextFirstMondayAfterAGap()
    {
        // Activated on the first Monday of April 2025.
        this.Time.Set( new DateTime( 2025, 4, 7, 0, 0, 0, DateTimeKind.Utc ) );
        this.TelemetryConfigurationService.EnsureActivated();
        var initialSalt = this.TelemetryConfigurationService.GetSalt( TelemetrySaltKind.Matomo );

        // Not used again until the 1st of June, a Sunday. June's first Monday is the 2nd and has not arrived, so the
        // salt is held although May's boundary went by unobserved.
        this.Time.Set( new DateTime( 2025, 6, 1, 0, 0, 0, DateTimeKind.Utc ) );
        Assert.Equal( initialSalt, this.TelemetryConfigurationService.GetSalt( TelemetrySaltKind.Matomo ) );

        // June's first Monday rotates it, once.
        this.Time.Set( new DateTime( 2025, 6, 2, 0, 0, 0, DateTimeKind.Utc ) );
        var rotatedSalt = this.TelemetryConfigurationService.GetSalt( TelemetrySaltKind.Matomo );
        Assert.NotEqual( initialSalt, rotatedSalt );

        // And it stays rotated for the rest of the month, so the gap costs one rotation and not two.
        this.Time.Set( new DateTime( 2025, 6, 30, 0, 0, 0, DateTimeKind.Utc ) );
        Assert.Equal( rotatedSalt, this.TelemetryConfigurationService.GetSalt( TelemetrySaltKind.Matomo ) );
    }

    /// <summary>
    /// A salt renewed on a first Monday is held until the next one, including through the days at the start of the
    /// following month that precede it.
    /// </summary>
    [Fact]
    public void SaltIsHeldUntilTheNextFirstMonday()
    {
        // Activated on the first Monday of May 2025.
        this.Time.Set( new DateTime( 2025, 5, 5, 0, 0, 0, DateTimeKind.Utc ) );
        this.TelemetryConfigurationService.EnsureActivated();
        var initialSalt = this.TelemetryConfigurationService.GetSalt( TelemetrySaltKind.Matomo );

        this.Time.Set( new DateTime( 2025, 5, 31, 0, 0, 0, DateTimeKind.Utc ) );
        Assert.Equal( initialSalt, this.TelemetryConfigurationService.GetSalt( TelemetrySaltKind.Matomo ) );

        this.Time.Set( new DateTime( 2025, 6, 1, 0, 0, 0, DateTimeKind.Utc ) );
        Assert.Equal( initialSalt, this.TelemetryConfigurationService.GetSalt( TelemetrySaltKind.Matomo ) );

        this.Time.Set( new DateTime( 2025, 6, 2, 0, 0, 0, DateTimeKind.Utc ) );
        Assert.NotEqual( initialSalt, this.TelemetryConfigurationService.GetSalt( TelemetrySaltKind.Matomo ) );
    }

    /// <summary>
    /// The device identifier is rotated with the salts and not separately, so that the hashes derived from it and
    /// the salted hashes always belong to the same period.
    /// </summary>
    [Fact]
    public void TheDeviceIdRotatesWithTheSalts()
    {
        this.Time.Set( new DateTime( 2025, 4, 7, 0, 0, 0, DateTimeKind.Utc ) );
        this.TelemetryConfigurationService.EnsureActivated();

        var initialDeviceId = this.TelemetryConfigurationService.DeviceId;
        var initialSalt = this.TelemetryConfigurationService.GetSalt( TelemetrySaltKind.Matomo );

        this.Time.Set( new DateTime( 2025, 5, 5, 0, 0, 0, DateTimeKind.Utc ) );

        Assert.NotEqual( initialSalt, this.TelemetryConfigurationService.GetSalt( TelemetrySaltKind.Matomo ) );
        Assert.NotEqual( initialDeviceId, this.TelemetryConfigurationService.DeviceId );
    }

    [Theory]
    [InlineData( false )]
    [InlineData( true )]
    public void DiagnosticSaltRotation( bool exceptionReporting )
    {
        // The first-party-only diagnostic salts (#1668) must rotate on the same monthly cadence as MatomoSalt/DeviceId.
        long GetDiagnosticSalt()
            => exceptionReporting
                ? this.TelemetryConfigurationService.GetSalt( TelemetrySaltKind.ExceptionReport )
                : this.TelemetryConfigurationService.GetSalt( TelemetrySaltKind.UsageTracking );

        this.Time.Set( new DateTime( 2025, 4, 10, 0, 0, 0, DateTimeKind.Utc ) );
        this.TelemetryConfigurationService.EnsureActivated();
        var initialDiagnosticSalt = GetDiagnosticSalt();

        // No rotation before the first Monday of May (the 5th).
        this.Time.Set( new DateTime( 2025, 4, 30, 0, 0, 0, DateTimeKind.Utc ) );
        Assert.Equal( initialDiagnosticSalt, GetDiagnosticSalt() );

        this.Time.Set( new DateTime( 2025, 5, 4, 0, 0, 0, DateTimeKind.Utc ) );
        Assert.Equal( initialDiagnosticSalt, GetDiagnosticSalt() );

        // Now there should be a change, in lockstep with MatomoSalt.
        this.Time.Set( new DateTime( 2025, 5, 5, 0, 0, 0, DateTimeKind.Utc ) );
        Assert.NotEqual( initialDiagnosticSalt, GetDiagnosticSalt() );
    }

    [Theory]
    [InlineData( TelemetrySaltKind.Matomo )]
    [InlineData( TelemetrySaltKind.UsageTracking )]
    [InlineData( TelemetrySaltKind.ExceptionReport )]
    [InlineData( TelemetrySaltKind.LicenseAudit )]
    public void GetSaltThrowsWhenNotActivated( TelemetrySaltKind kind )
    {
        // Reading a salt before telemetry has been activated must fail loudly rather than silently return a zeroed salt
        // (which would make the pseudonyms identical across all not-yet-activated machines). The caller is responsible
        // for calling EnsureActivated() first. See #1711, #1701.
        Assert.False( this.TelemetryConfigurationService.IsActivated );

        Assert.Throws<InvalidOperationException>( () => this.TelemetryConfigurationService.GetSalt( kind ) );

        // After activation, the salt is available and non-zero.
        this.TelemetryConfigurationService.EnsureActivated();
        Assert.NotEqual( 0L, this.TelemetryConfigurationService.GetSalt( kind ) );
    }

    [Fact]
    public void SaltsAreGeneratedAndMutuallyDistinct()
    {
        // Each salt must be a non-zero value, and the three salts must be mutually distinct, so that the Matomo,
        // usage-tracking and exception-reporting pseudonyms cannot be correlated with one another (#1668).
        this.Time.Set( new DateTime( 2025, 4, 10, 0, 0, 0, DateTimeKind.Utc ) );
        this.TelemetryConfigurationService.EnsureActivated();

        var matomoSalt = this.TelemetryConfigurationService.GetSalt( TelemetrySaltKind.Matomo );
        var usageTrackingSalt = this.TelemetryConfigurationService.GetSalt( TelemetrySaltKind.UsageTracking );
        var exceptionReportingSalt = this.TelemetryConfigurationService.GetSalt( TelemetrySaltKind.ExceptionReport );

        Assert.NotEqual( 0, matomoSalt );
        Assert.NotEqual( 0, usageTrackingSalt );
        Assert.NotEqual( 0, exceptionReportingSalt );

        Assert.NotEqual( matomoSalt, usageTrackingSalt );
        Assert.NotEqual( matomoSalt, exceptionReportingSalt );
        Assert.NotEqual( usageTrackingSalt, exceptionReportingSalt );
    }

    [Fact]
    public void DeviceHashesAreMutuallyUncorrelatable()
    {
        // For one device, the hash sent to Matomo (keyed by MatomoSalt) and the two first-party hashes — the
        // usage-tracking <Machine> (keyed by UsageTrackingSalt) and the exception-reporting <ClientId> (keyed by
        // ExceptionReportingSalt) — must all differ, so none of the three datasets shares a join key. #1668.
        this.Time.Set( new DateTime( 2025, 4, 10, 0, 0, 0, DateTimeKind.Utc ) );
        this.TelemetryConfigurationService.EnsureActivated();

        var deviceId = this.TelemetryConfigurationService.DeviceId.ToString();
        var matomoHash = HashUtilities.ComputeInt64Hmac( deviceId, this.TelemetryConfigurationService.GetSalt( TelemetrySaltKind.Matomo ) );
        var usageTrackingHash = HashUtilities.ComputeInt64Hmac( deviceId, this.TelemetryConfigurationService.GetSalt( TelemetrySaltKind.UsageTracking ) );
        var exceptionReportingHash = HashUtilities.ComputeInt64Hmac( deviceId, this.TelemetryConfigurationService.GetSalt( TelemetrySaltKind.ExceptionReport ) );

        Assert.NotEqual( matomoHash, usageTrackingHash );
        Assert.NotEqual( matomoHash, exceptionReportingHash );
        Assert.NotEqual( usageTrackingHash, exceptionReportingHash );
    }

    [Theory]
    [InlineData( "matomo" )]
    [InlineData( "usage" )]
    [InlineData( "exception" )]
    public void SaltDoesNotDependOnSeededRandomNumberGenerator( string saltKind )
    {
        // The test RNG service (SharpCrafters.Backstage.Infrastructure.RandomNumberGenerator) is seeded
        // deterministically (seed 0, see TestsBase). Each anonymization salt is a security-sensitive value
        // (the HMAC-SHA256 key for the device/licensee hashes) and must be generated by a CSPRNG (#1654), so it
        // must NOT be reproducible from the seeded RNG service. Two independent service providers sharing the
        // same RNG seed must therefore produce different salts.
        long GenerateSalt()
        {
            var serviceProvider = this.CloneServiceCollection().BuildServiceProvider().InitializeBackstageServices();
            var telemetryConfigurationService = serviceProvider.GetRequiredBackstageService<ITelemetryConfigurationService>();
            telemetryConfigurationService.EnsureActivated();

            return saltKind switch
            {
                "matomo" => telemetryConfigurationService.GetSalt( TelemetrySaltKind.Matomo ),
                "usage" => telemetryConfigurationService.GetSalt( TelemetrySaltKind.UsageTracking ),
                "exception" => telemetryConfigurationService.GetSalt( TelemetrySaltKind.ExceptionReport ),
                _ => throw new ArgumentOutOfRangeException( nameof(saltKind) )
            };
        }

        var salt1 = GenerateSalt();
        var salt2 = GenerateSalt();

        Assert.NotEqual( salt1, salt2 );
    }

    [Fact]
    public void ResetReportedIssuesClearsTheDedupStore()
    {
        // reset-dedup (#1684): clearing the record of already-reported issues lets an issue that was previously
        // reported (and therefore deduplicated by the exception reporter) be captured and surfaced again. See #1674.
        var configurationManager = this.ConfigurationManager!;

        configurationManager.Update<TelemetryConfiguration>( c => c with { Issues = c.Issues.SetItem( "some-issue-hash", ReportingStatus.Reported ) } );

        Assert.NotEmpty( configurationManager.Get<TelemetryConfiguration>().Issues );

        this.TelemetryConfigurationService.ResetReportedIssues();

        Assert.Empty( configurationManager.Get<TelemetryConfiguration>().Issues );
    }
}