// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using System.Diagnostics;

namespace SharpCrafters.Backstage.Maintenance;

/// <summary>
/// A process that matches a <see cref="ProcessSpec"/>, as <see cref="IProcessManager.GetMatchingProcesses"/> selects it.
/// What is done with it is decided by the <see cref="IProcessShutdownStrategy"/> that asked for it.
/// </summary>
/// <remarks>
/// It does not own <see cref="Process"/>, which the caller of <see cref="IProcessManager.GetMatchingProcesses"/> disposes.
/// </remarks>
internal sealed class MatchedProcess
{
    public MatchedProcess( Process process, ProcessSpec spec, string? mainModule )
    {
        this.Process = process;
        this.Spec = spec;
        this.MainModule = mainModule;
    }

    public Process Process { get; }

    public ProcessSpec Spec { get; }

    /// <summary>
    /// Gets the path of the assembly that a <c>dotnet</c> process runs, or <c>null</c> for a process that runs as its own
    /// executable.
    /// </summary>
    public string? MainModule { get; }
}
