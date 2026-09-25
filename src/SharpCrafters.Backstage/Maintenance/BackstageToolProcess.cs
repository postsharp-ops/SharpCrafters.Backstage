// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using JetBrains.Annotations;
using SharpCrafters.Backstage.Tools;
using System;
using System.Diagnostics;

namespace SharpCrafters.Backstage.Maintenance;

/// <summary>
/// A running process of one of the tools of the product, returned by <see cref="IProcessManager.GetToolProcesses"/>.
/// </summary>
/// <remarks>
/// This object owns <see cref="Process"/> and disposes it when it is disposed. It is disposed by the
/// <see cref="BackstageToolProcessCollection"/> that contains it.
/// </remarks>
[PublicAPI]
public sealed class BackstageToolProcess : IDisposable
{
    internal BackstageToolProcess( BackstageTool tool, Process process )
    {
        this.Tool = tool;
        this.Process = process;
    }

    /// <summary>
    /// Gets the tool that the process runs: <see cref="BackstageTool.Worker"/> or <see cref="BackstageTool.DesktopWindows"/>.
    /// </summary>
    public BackstageTool Tool { get; }

    /// <summary>
    /// Gets the process. It is disposed with this object.
    /// </summary>
    public Process Process { get; }

    public void Dispose() => this.Process.Dispose();
}
