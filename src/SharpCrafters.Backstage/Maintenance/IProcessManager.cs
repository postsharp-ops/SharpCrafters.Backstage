// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Extensibility;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;

namespace SharpCrafters.Backstage.Maintenance;

/// <summary>
/// Finds the processes that match a list of <see cref="ProcessSpec"/>, for the implementations of
/// <see cref="IProcessShutdownStrategy"/> in this package.
/// </summary>
internal interface IProcessManager : IBackstageService
{
    /// <summary>
    /// Gets the processes that may match one of <paramref name="processSpecs"/>. The caller owns them, and disposes all of
    /// them once it has acted on the ones that <see cref="GetMatchingProcesses"/> selects.
    /// </summary>
    List<Process> GetCandidateProcesses( ImmutableArray<ProcessSpec> processSpecs );

    /// <summary>
    /// Selects, among <paramref name="candidates"/>, the processes that match one of <paramref name="processSpecs"/>, except
    /// the current process and its parents.
    /// </summary>
    IEnumerable<MatchedProcess> GetMatchingProcesses( IEnumerable<Process> candidates, ImmutableArray<ProcessSpec> processSpecs );
}
