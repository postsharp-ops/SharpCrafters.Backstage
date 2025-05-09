// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using Metalama.Backstage.Application;
using Metalama.Backstage.Diagnostics;
using Metalama.Backstage.Extensibility;
using Metalama.Backstage.Infrastructure;
using Metalama.Backstage.Licensing;
using Metalama.Backstage.Telemetry.Metrics;
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Metalama.Backstage.Telemetry;

internal sealed class UsageSession : IUsageSession
{
    private readonly string _kind;
    private readonly TelemetryReportUploader _telemetryReportUploader;
    private readonly ILogger _logger;
    private bool _isDisposed;

    public MetricCollection Metrics { get; }

    public UsageSession( IServiceProvider serviceProvider, string kind )
    {
        this._kind = kind;
        this.Metrics = new MetricCollection();
        this._telemetryReportUploader = serviceProvider.GetRequiredBackstageService<TelemetryReportUploader>();
        this._logger = serviceProvider.GetLoggerFactory().Telemetry();

        this.InitializeMetrics( serviceProvider );

        if ( this._logger.Trace != null )
        {
            this._logger.Trace.Log( $"Usage session started." );
            this.TraceSample();
        }
    }

    private void InitializeMetrics( IServiceProvider serviceProvider )
    {
        var time = serviceProvider.GetRequiredBackstageService<IDateTimeProvider>();

        var applicationInfo = serviceProvider.GetRequiredBackstageService<IApplicationInfoProvider>().CurrentApplication;
        var reportedComponent = applicationInfo.GetLatestComponentMadeByPostSharp();

        var loggerFactory = serviceProvider.GetLoggerFactory();

        this.Metrics.Add( new StringMetric( "MetricsEventKind", this._kind ) );

        this.Metrics.Add( new Int32Metric( "Processor.Count", Environment.ProcessorCount ) );
        this.Metrics.Add( new StringMetric( "Processor.Architecture", RuntimeInformation.ProcessArchitecture.ToString() ) );

        this.Metrics.Add( new StringMetric( "OS.Platform", RuntimeInformation.OSDescription ) );

        this.Metrics.Add( new StringMetric( "Net.Architecture", RuntimeInformation.ProcessArchitecture.ToString() ) );
        this.Metrics.Add( new StringMetric( "Net.Version", Environment.Version.ToString() ) );

        this.Metrics.Add( new StringMetric( "Application.Name", reportedComponent.Name ) );
        this.Metrics.Add( new StringMetric( "Application.Version", reportedComponent.PackageVersion ) );
        this.Metrics.Add( new BoolMetric( "Application.IsUnattended", applicationInfo.IsUnattendedProcess( loggerFactory ) ) );
        this.Metrics.Add( new StringMetric( "Application.ProcessName", Process.GetCurrentProcess().ProcessName ) );
        this.Metrics.Add( new StringMetric( "Application.ProcessKind", applicationInfo.ProcessKind.ToString() ) );
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

        var report = new UsageTelemetryReport( this.Metrics );
        this._telemetryReportUploader.Upload( report );
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