// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using System;
using System.Collections.Generic;

namespace SharpCrafters.Backstage.VersionControl;

/// <summary>
/// The result of one version control query over one repository: the instant at which the query was started, and the
/// files that it reported as modified.
/// </summary>
/// <remarks>
/// <para>
/// What is recorded is the answer of the version control tool about the <i>repository</i>, not the verdict about a
/// project. That is what lets the record be shared: the projects of a solution query disjoint sets of files but live
/// in the same repository, so one record serves all of them, and each derives its own verdict by intersecting its own
/// file list with <see cref="ModifiedFiles"/>. A record holding the queried file list instead would be invalidated by
/// the next project of the same repository, and the repository would be queried once per project.
/// </para>
/// <para>
/// <see cref="Timestamp"/> is taken <i>before</i> the tool is started, never after. A record stamped with the instant
/// at which it was written would cover the interval during which the tool was running, and a file modified in that
/// interval would be considered unmodified for as long as the record lived.
/// </para>
/// </remarks>
internal sealed class VcsStatusRecord
{
    /// <summary>
    /// The instant, in UTC, at which the query was started. Everything written at or after this instant is outside
    /// what the record covers.
    /// </summary>
    public DateTime Timestamp { get; }

    /// <summary>
    /// The full paths of the files that the query reported as modified, in the spelling of the caller.
    /// </summary>
    public IReadOnlyCollection<string> ModifiedFiles { get; }

    public VcsStatusRecord( DateTime timestamp, IReadOnlyCollection<string> modifiedFiles )
    {
        this.Timestamp = timestamp;
        this.ModifiedFiles = modifiedFiles;
    }
}
