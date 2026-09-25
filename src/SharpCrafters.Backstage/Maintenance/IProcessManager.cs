// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using JetBrains.Annotations;
using SharpCrafters.Backstage.Extensibility;
using System.Collections.Generic;

namespace SharpCrafters.Backstage.Maintenance;

[PublicAPI]
public interface IProcessManager : IBackstageService
{
    void KillCompilerProcesses( bool shouldEmitWarnings );

    /// <summary>
    /// Ends the running processes of the tools of the product: the worker, whether it uploads the telemetry or hosts
    /// the setup web server, and the desktop notifier. A product calls it to release the files that these processes
    /// hold, for instance from a <c>shutdown</c> command.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The tools hold no state that ending them can lose. The worker deletes the telemetry files it uploads only once
    /// they have been sent, so an upload that is ended is made again the next time.
    /// </para>
    /// <para>
    /// The processes are identified as <see cref="KillCompilerProcesses"/> identifies them, for the product of the
    /// calling application. A process whose modules cannot be read, typically because it belongs to another user, is
    /// not found and not reported. The calling process is never ended, so that a tool can call this method too.
    /// </para>
    /// </remarks>
    /// <returns>One result per process found, in no particular order. The list is empty when no tool was running.</returns>
    IReadOnlyList<ToolProcessShutdownResult> ShutDownToolProcesses();
}