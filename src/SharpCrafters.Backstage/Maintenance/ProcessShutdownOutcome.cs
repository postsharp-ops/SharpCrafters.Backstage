// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using JetBrains.Annotations;

namespace SharpCrafters.Backstage.Maintenance;

/// <summary>
/// What became of a process that an <see cref="IProcessShutdownStrategy"/> found.
/// </summary>
[PublicAPI]
public enum ProcessShutdownOutcome
{
    /// <summary>The process exited, on request or on its own.</summary>
    Exited,

    /// <summary>The process was ended.</summary>
    Ended,

    /// <summary>The process is still running, typically because it did not exit in time and was not to be ended.</summary>
    StillRunning,

    /// <summary>The process could not be reached or acted on, for instance because it belongs to another user.</summary>
    NotActedOn,

    /// <summary>
    /// The process is reported and deliberately left alone, because it belongs to an application that the user works in,
    /// for instance an integrated development environment, and <see cref="ProcessShutdownOptions.All"/> was not set. It
    /// does not make the command fail.
    /// </summary>
    Reported
}
