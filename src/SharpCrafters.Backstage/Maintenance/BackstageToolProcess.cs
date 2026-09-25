// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using JetBrains.Annotations;
using SharpCrafters.Backstage.Tools;
using System.Diagnostics;

namespace SharpCrafters.Backstage.Maintenance;

/// <summary>
/// A running process of one of the tools of the product, returned by <see cref="IProcessManager.GetToolProcesses"/>.
/// </summary>
/// <param name="Tool">The tool that the process runs: <see cref="BackstageTool.Worker"/> or <see cref="BackstageTool.DesktopWindows"/>.</param>
/// <param name="Process">The process. The caller owns it and disposes it.</param>
[PublicAPI]
public sealed record BackstageToolProcess( BackstageTool Tool, Process Process );
