// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using JetBrains.Annotations;
using SharpCrafters.Backstage.Tools;

namespace SharpCrafters.Backstage.Maintenance;

/// <summary>
/// What <see cref="IProcessManager.ShutDownToolProcesses"/> did with one process of a tool of the product.
/// </summary>
/// <param name="Tool">The tool that the process ran: <see cref="BackstageTool.Worker"/> or <see cref="BackstageTool.DesktopWindows"/>.</param>
/// <param name="ProcessId">The identifier of the process.</param>
/// <param name="HasExited"><c>true</c> when the process has exited, <c>false</c> when it could not be ended.</param>
/// <param name="ErrorMessage">Why the process could not be ended, or <c>null</c> when it has exited.</param>
[PublicAPI]
public sealed record ToolProcessShutdownResult( BackstageTool Tool, int ProcessId, bool HasExited, string? ErrorMessage );
