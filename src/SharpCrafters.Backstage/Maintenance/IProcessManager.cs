// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using JetBrains.Annotations;
using SharpCrafters.Backstage.Extensibility;

namespace SharpCrafters.Backstage.Maintenance;

[PublicAPI]
public interface IProcessManager : IBackstageService
{
    void KillCompilerProcesses( bool shouldEmitWarnings );

    /// <summary>
    /// Gets the running processes of the tools of the product: the worker, whether it uploads the telemetry or hosts
    /// the setup web server, and the desktop notifier.
    /// </summary>
    /// <remarks>
    /// The processes are identified as <see cref="KillCompilerProcesses"/> identifies them. A process whose modules
    /// cannot be read, typically because it belongs to another user, is not returned.
    /// </remarks>
    /// <returns>The processes. The caller disposes the collection, which disposes the processes.</returns>
    BackstageToolProcessCollection GetToolProcesses();
}