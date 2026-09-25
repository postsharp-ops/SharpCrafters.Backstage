// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.ProcessClassification;
using System.Collections.Immutable;

namespace SharpCrafters.Backstage.Application
{
    /// <summary>
    /// Provides version information about an application.
    /// </summary>
    public interface IApplicationInfo : IComponentInfo
    {
        /// <summary>
        /// Gets the kind of the process that the application runs in, or <see langword="null"/> when the kind must be
        /// detected from the name and the command line of the process. Read the kind from
        /// <see cref="IApplicationInfoProvider.ProcessKind"/>, which performs that detection when this property is
        /// <see langword="null"/>.
        /// </summary>
        ProcessKind? ProcessKind { get; }

        bool IsLongRunningProcess { get; }

        /// <summary>
        /// Gets a value indicating whether the application is the background worker process of a product. The worker
        /// has no user interface, so <see cref="IUnattendedProcessDetector"/> classifies it as unattended without
        /// examining the process.
        /// </summary>
        bool IsWorkerProcess { get; }

        /// <summary>
        /// Gets a value indicating whether telemetry is enabled for the application.
        /// </summary>
        bool IsTelemetryEnabled { get; }

        /// <summary>
        /// Gets a value indicating whether licenses should be audited by this application.
        /// </summary>
        bool IsLicenseAuditEnabled { get; }

        /// <summary>
        /// Gets a value indicating whether crashes should be reported for the application.
        /// </summary>
        bool ShouldCreateLocalCrashReports { get; }

        /// <summary>
        /// Gets the list of additional components of the application.
        /// </summary>
        ImmutableArray<IComponentInfo> Components { get; }
    }
}