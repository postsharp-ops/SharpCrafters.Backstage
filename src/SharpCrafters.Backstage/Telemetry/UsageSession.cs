// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Application;
using SharpCrafters.Backstage.Configuration;
using SharpCrafters.Backstage.Diagnostics;
using SharpCrafters.Backstage.Extensibility;
using SharpCrafters.Backstage.Infrastructure;
using SharpCrafters.Backstage.ProcessClassification;
using SharpCrafters.Backstage.Telemetry.Metrics;
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace SharpCrafters.Backstage.Telemetry;

internal sealed class UsageSession : IUsageSession
{
    private readonly IServiceProvider _serviceProvider;
    private readonly string _kind;
    private readonly TelemetryReportUploader _telemetryReportUploader;
    private readonly ILogger _logger;
    private readonly MatomoUploader? _matomoUploader;
    private readonly IDateTimeProvider _time;
    private readonly IConfigurationManager _configurationManager;
    private readonly BackstageBackgroundTasksService _backgroundTasksService;
    private readonly IUnattendedProcessDetector _unattendedProcessDetector;

    private bool _isDisposed;

    public bool ShouldCollectMetrics { get; }

    public MetricCollection Metrics { get; }

    public UsageSession( IServiceProvider serviceProvider, string kind, bool shouldCollectMetrics )
    {
        this.ShouldCollectMetrics = shouldCollectMetrics;
        this._serviceProvider = serviceProvider;
        this._kind = kind;

        this._telemetryReportUploader = serviceProvider.GetRequiredBackstageService<TelemetryReportUploader>();
        this._logger = serviceProvider.GetLoggerFactory().Telemetry();
        this._matomoUploader = serviceProvider.GetBackstageService<MatomoUploader>();
        this._configurationManager = serviceProvider.GetRequiredBackstageService<IConfigurationManager>();
        this._time = serviceProvider.GetRequiredBackstageService<IDateTimeProvider>();
        this._backgroundTasksService = serviceProvider.GetRequiredBackstageService<BackstageBackgroundTasksService>();
        this._unattendedProcessDetector = serviceProvider.GetRequiredBackstageService<IUnattendedProcessDetector>();

        // Initialize the Metrics collection.
        if ( shouldCollectMetrics )
        {
            this.Metrics = new MetricCollection();
            this.InitializeMetrics( serviceProvider );
        }
        else
        {
            this.Metrics = MetricCollection.EmptyReadOnly;
        }

        if ( this._logger.Trace != null )
        {
            this._logger.Trace.Log( $"Usage session started." );
            this.TraceSample();
        }
    }

    private void InitializeMetrics( IServiceProvider serviceProvider )
    {
        var time = serviceProvider.GetRequiredBackstageService<IDateTimeProvider>();

        var applicationInfoProvider = serviceProvider.GetRequiredBackstageService<IApplicationInfoProvider>();
        var applicationInfo = applicationInfoProvider.Application;
        var reportedComponent = applicationInfo.GetLatestVendorComponent( serviceProvider.GetRequiredBackstageService<ProductProfile>().Company );

        this.Metrics.Add( new StringMetric( "MetricsEventKind", this._kind ) );

        this.Metrics.Add( new Int32Metric( "Processor.Count", Environment.ProcessorCount ) );
        this.Metrics.Add( new StringMetric( "Processor.Architecture", RuntimeInformation.ProcessArchitecture.ToString() ) );

        this.Metrics.Add( new StringMetric( "OS.Platform", RuntimeInformation.OSDescription ) );

        this.Metrics.Add( new StringMetric( "Net.Architecture", RuntimeInformation.ProcessArchitecture.ToString() ) );
        this.Metrics.Add( new StringMetric( "Net.Version", Environment.Version.ToString() ) );

        this.Metrics.Add( new StringMetric( "Application.Name", reportedComponent.Name ) );
        this.Metrics.Add( new StringMetric( "Application.Version", reportedComponent.PackageVersion ) );
        this.Metrics.Add( new BoolMetric( "Application.IsUnattended", this._unattendedProcessDetector.IsCurrentProcessUnattended ) );
        this.Metrics.Add( new StringMetric( "Application.ProcessName", Process.GetCurrentProcess().ProcessName ) );
        this.Metrics.Add( new StringMetric( "Application.ProcessKind", applicationInfoProvider.ProcessKind.ToString() ) );
        this.Metrics.Add( new StringMetric( "Application.EntryAssembly", Path.GetFileName( Assembly.GetEntryAssembly()?.Location ) ) );

        this.Metrics.Add( new DateTimeMetric( "Time", time.UtcNow ) );
    }

    public void Dispose()
    {
        if ( this._isDisposed )
        {
            return;
        }

        this._isDisposed = true;

        if ( this._logger.Trace != null )
        {
            this._logger.Trace.Log( $"Usage session ended." );
            this.TraceSample();
        }

        this.Metrics.Freeze();

        UsageTelemetryReport? report = null;

        UsageTelemetryReport GetReport() => report ??= report = new UsageTelemetryReport( this._serviceProvider, this.Metrics );

        if ( this.ShouldCollectMetrics )
        {
            this._telemetryReportUploader.Upload( GetReport() );
        }

        if ( this._matomoUploader != null )
        {
            var actionName = this._serviceProvider.GetRequiredBackstageService<IApplicationInfoProvider>().Application.UsageAnalyticsActionName;

            if ( this.TryClaimDailyAnalyticsReport( GetDailyAnalyticsBucket( actionName, GetReport().AssemblyVersion ) ) )
            {
                this._backgroundTasksService.Enqueue( () => this._matomoUploader.SendAsync( GetReport(), actionName, newVisit: true ) );
            }
        }
    }

    /// <summary>
    /// Gets the key under which the daily usage report of an action and of a version line is claimed.
    /// </summary>
    /// <param name="actionName">The action of the application, given by <see cref="IApplicationInfo.UsageAnalyticsActionName"/>.</param>
    /// <param name="version">The version of the reported component, which is the latest component of the vendor.</param>
    /// <returns>The action followed by the major and minor version, for instance <c>usage:2027.0</c>.</returns>
    /// <remarks>
    /// The major and minor version identify the version line. They are also the version that the analytics service
    /// receives. All builds of a version line therefore share one daily report, and that report carries the version of
    /// the latest component of the vendor in the process.
    /// </remarks>
    internal static string GetDailyAnalyticsBucket( string actionName, Version? version )
        => version == null ? actionName : $"{actionName}:{version.ToString( 2 )}";

    /// <summary>
    /// Determines whether the daily usage report of a bucket must be sent now, and records that it is.
    /// </summary>
    /// <param name="bucket">The bucket, given by <see cref="GetDailyAnalyticsBucket"/>.</param>
    /// <returns><see langword="true"/> if no report of this bucket was sent during the last day.</returns>
    /// <remarks>
    /// The claim is recorded in <see cref="TelemetryConfiguration.LastMatomoPostTimes"/>. Versions earlier than 2027.0
    /// record their claim in <see cref="TelemetryConfiguration.LastMatomoPostTime"/>. This version neither reads nor
    /// writes that property, so that each version line is counted separately.
    /// </remarks>
    private bool TryClaimDailyAnalyticsReport( string bucket )
    {
        var now = this._time.UtcNow;
        var threshold = now.AddDays( -1 );

        return this._configurationManager.UpdateIf<TelemetryConfiguration>(
            c => !c.LastMatomoPostTimes.TryGetValue( bucket, out var lastPostTime ) || lastPostTime <= threshold,
            c => c with { LastMatomoPostTimes = c.LastMatomoPostTimes.SetItem( bucket, now ) } );
    }

    private void TraceSample()
    {
        if ( this._logger.Trace == null )
        {
            return;
        }

        foreach ( var metric in this.Metrics )
        {
            this._logger.Trace.Log( $"  {metric.Name}: {metric}" );
        }
    }
}