// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Extensibility;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace SharpCrafters.Backstage.Maintenance;

/// <summary>
/// Finds the processes that match a list of <see cref="ProcessSpec"/>, for the implementations of
/// <see cref="IProcessShutdownStrategy"/> in this package.
/// </summary>
internal interface IProcessManager : IBackstageService
{
    /// <summary>
    /// Gets the running processes that match one of <paramref name="processSpecs"/>, except the current process and its
    /// parents.
    /// </summary>
    /// <returns>
    /// The matching processes. The caller owns them and disposes their <see cref="MatchedProcess.Process"/>. The processes
    /// that were examined and do not match are disposed before the method returns.
    /// </returns>
    IReadOnlyList<MatchedProcess> GetMatchingProcesses( ImmutableArray<ProcessSpec> processSpecs );
}
