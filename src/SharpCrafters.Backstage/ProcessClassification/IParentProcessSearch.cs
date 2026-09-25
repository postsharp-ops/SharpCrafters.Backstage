// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Extensibility;
using System.Collections.Generic;

namespace SharpCrafters.Backstage.ProcessClassification;

/// <summary>
/// Lists the ancestors of the current process.
/// </summary>
public interface IParentProcessSearch : IBackstageService
{
    /// <summary>
    /// Gets the ancestors of the current process, starting with its parent.
    /// </summary>
    /// <param name="pivots">
    /// An optional set of process names. When it is specified, the search stops after the first ancestor whose name
    /// is in the set.
    /// </param>
    /// <returns>The ancestors of the current process, starting with its parent.</returns>
    IReadOnlyList<ProcessInfo> GetParentProcesses( ISet<string>? pivots = null );
}
