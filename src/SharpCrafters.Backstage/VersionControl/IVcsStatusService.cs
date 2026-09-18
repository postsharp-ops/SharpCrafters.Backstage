// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using JetBrains.Annotations;
using SharpCrafters.Backstage.Extensibility;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SharpCrafters.Backstage.VersionControl;

/// <summary>
/// Reports whether any of a given set of files is modified in version control, so that a caller can treat an
/// unmodified source tree differently from one that the user is working on.
/// </summary>
/// <remarks>
/// <para>
/// A file counts as modified only when git reports a content modification of a tracked file, staged or unstaged.
/// Untracked, added, deleted, renamed and ignored files do not count, and neither do files that belong to no git
/// repository.
/// </para>
/// <para>
/// The rule follows one distinction: whether the user controls the condition. A global failure, such as git not being
/// installed or the project belonging to no repository, is reported as modified, which makes the condition visible.
/// A file-specific inconsistency, such as a generated file or a file shipped by a package, is tolerated, because the
/// user cannot act on it.
/// </para>
/// <para>
/// The complete doctrine, the reasoning behind each rule and the accepted limits are in <c>docs/vcs-check.md</c>.
/// </para>
/// </remarks>
[PublicAPI]
public interface IVcsStatusService : IBackstageService
{
    /// <summary>
    /// Determines whether at least one of the given files is modified in version control, <b>or whether the status
    /// could not be determined</b>.
    /// </summary>
    /// <param name="filePaths">The absolute paths of the files to test. Relative paths are resolved against the
    /// current directory of the process, which a build host changes from one project to the next, so a caller is
    /// expected to pass absolute paths.</param>
    /// <param name="cancellationToken">A token that abandons the query, including the version control command that
    /// it runs.</param>
    /// <returns><c>true</c> when at least one file is modified, when no file belongs to a repository, or when the
    /// status could not be determined; <c>false</c> only when every file is known to be unmodified.</returns>
    ValueTask<bool> IsAnyFileModifiedAsync( IReadOnlyCollection<string> filePaths, CancellationToken cancellationToken = default );
}
