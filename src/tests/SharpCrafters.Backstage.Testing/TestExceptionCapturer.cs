// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using JetBrains.Annotations;
using SharpCrafters.Backstage.Diagnostics;
using SharpCrafters.Backstage.Telemetry;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace SharpCrafters.Backstage.Testing;

/// <summary>
/// An exception report that <see cref="TestExceptionCapturer"/> received.
/// </summary>
/// <param name="Exception">The reported exception.</param>
/// <param name="Kind">The kind of report.</param>
/// <param name="Consent">The consent that the telemetry context resolved for the report.</param>
/// <param name="WriteLocalReport">A value indicating whether the caller asked for a local crash report.</param>
[PublicAPI]
public sealed record RecordedExceptionReport( Exception Exception, ExceptionReportingKind Kind, TelemetryConsent Consent, bool WriteLocalReport );

/// <summary>
/// A test exception capturer that records each captured report instead of queuing it for upload.
/// </summary>
/// <remarks>
/// <para>
/// A telemetry context calls the capturer only when its policy allows telemetry for the scenario. A test therefore
/// asserts that <see cref="CapturedReports"/> is empty to prove that a disabled policy sends no exception report.
/// </para>
/// <para>
/// An exception has two outputs: the local crash report, which is written regardless of the policy, and the report for
/// upload, which the policy controls. When the policy allows telemetry, the real capturer writes the local crash report
/// itself. When the capturer is created with a <see cref="LocalExceptionReporter"/>, it writes the local crash report in the
/// same way, so that a test sees the local crash report whatever the policy is.
/// </para>
/// </remarks>
[PublicAPI]
public class TestExceptionCapturer : IExceptionCapturer
{
    private readonly ConcurrentQueue<RecordedExceptionReport> _capturedReports = new();
    private readonly LocalExceptionReporter? _localExceptionReporter;

    /// <summary>
    /// Initializes a new instance of the <see cref="TestExceptionCapturer"/> class that writes no local crash report.
    /// </summary>
    public TestExceptionCapturer() { }

    /// <summary>
    /// Initializes a new instance of the <see cref="TestExceptionCapturer"/> class that writes the local crash report through
    /// the given reporter, as the real capturer does.
    /// </summary>
    internal TestExceptionCapturer( LocalExceptionReporter localExceptionReporter )
    {
        this._localExceptionReporter = localExceptionReporter;
    }

    /// <summary>
    /// Gets the reports that were captured, in the order of capture.
    /// </summary>
    public IReadOnlyCollection<RecordedExceptionReport> CapturedReports => this._capturedReports;

    public void Capture(
        ClassifiedException classifiedException,
        ExceptionReportingKind exceptionReportingKind,
        TelemetryConsent telemetryConsent,
        bool writeLocalReport,
        IExceptionAdapter? adapter )
    {
        if ( classifiedException.IsError && exceptionReportingKind == ExceptionReportingKind.Exception && writeLocalReport )
        {
            this._localExceptionReporter?.ReportException( classifiedException.Exception );
        }

        this._capturedReports.Enqueue( new RecordedExceptionReport( classifiedException.Exception, exceptionReportingKind, telemetryConsent, writeLocalReport ) );
    }
}
