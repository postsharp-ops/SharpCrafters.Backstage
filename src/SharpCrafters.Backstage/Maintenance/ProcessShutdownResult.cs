// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using JetBrains.Annotations;

namespace SharpCrafters.Backstage.Maintenance;

/// <summary>
/// What an <see cref="IProcessShutdownStrategy"/> did with one process.
/// </summary>
/// <param name="Description">What the process is, for the report, for instance <c>MSBuild node</c>.</param>
/// <param name="ProcessId">The identifier of the process, or zero when it is not known.</param>
/// <param name="Outcome">What became of the process.</param>
/// <param name="Reason">Why the process is still running, was not acted on, or is reported, or <c>null</c>.</param>
[PublicAPI]
public sealed record ProcessShutdownResult( string Description, int ProcessId, ProcessShutdownOutcome Outcome, string? Reason = null )
{
    /// <summary>
    /// Gets a value indicating whether the process may still be running and hold files, which makes the command fail.
    /// </summary>
    public bool IsRunning => this.Outcome is ProcessShutdownOutcome.StillRunning or ProcessShutdownOutcome.NotActedOn;
}
