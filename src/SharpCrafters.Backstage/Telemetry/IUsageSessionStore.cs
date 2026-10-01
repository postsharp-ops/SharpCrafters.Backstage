// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using JetBrains.Annotations;
using SharpCrafters.Backstage.Extensibility;
using System;

namespace SharpCrafters.Backstage.Telemetry;

/// <summary>
/// Records when the usage of each project was last reported, so that at most one usage report is produced per project
/// and per period.
/// </summary>
/// <remarks>
/// <para>
/// Each project has a record of its own, and the records are read and written independently of each other and of the
/// telemetry configuration. Two processes therefore exclude each other only when they ask about the same project. The
/// records used to be a dictionary of <see cref="TelemetryConfiguration"/>, and every process of a parallel build then
/// waited for the lock of the whole configuration to record its project (issue 2092).
/// </para>
/// <para>
/// Which store holds the records is the product's to say: see <see cref="Application.BackstageProduct.RegisterServices"/>.
/// Metalama uses <see cref="FileUsageSessionStore"/>, and PostSharp uses <see cref="RegistryUsageSessionStore"/> on
/// Windows.
/// </para>
/// </remarks>
[PublicAPI]
public interface IUsageSessionStore : IBackstageService
{
    /// <summary>
    /// Determines whether the usage of a project must be reported now, and records that it is.
    /// </summary>
    /// <param name="projectKey">The name of the project, or the kind of the session when the project is unknown. The comparison is case-insensitive.</param>
    /// <param name="now">The current UTC time.</param>
    /// <param name="period">The minimal interval between two reports of the same project.</param>
    /// <returns>
    /// <see langword="true"/> if the caller must report the usage of the project. Of several concurrent callers asking
    /// about the same project, at most one obtains <see langword="true"/> for a given period.
    /// </returns>
    /// <remarks>
    /// This method never throws. A record that cannot be read or written yields <see langword="false"/>, because missing
    /// a usage report is a better outcome than failing the build that would have produced it.
    /// </remarks>
    bool TryClaim( string projectKey, DateTime now, TimeSpan period );
}
