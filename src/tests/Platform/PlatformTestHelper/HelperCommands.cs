// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

namespace SharpCrafters.Backstage.PlatformTests;

/// <summary>
/// The commands of the helper process, and the lines it writes to its standard output. A test waits for these lines
/// instead of waiting for a fixed time.
/// </summary>
public static class HelperCommands
{
    /// <summary>
    /// <c>hold-lock &lt;name&gt;</c>: acquires the named lock, writes <see cref="AcquiredLine"/>, and releases the lock
    /// when a line arrives on the standard input.
    /// </summary>
    public const string HoldLock = "hold-lock";

    /// <summary>
    /// <c>abandon-lock &lt;name&gt;</c>: acquires the named lock, writes <see cref="AcquiredLine"/>, and waits until the
    /// test kills the process, which abandons the lock.
    /// </summary>
    public const string AbandonLock = "abandon-lock";

    /// <summary>
    /// <c>print-parents</c>: writes one line per parent process, in the form <c>&lt;process id&gt; &lt;process name&gt;</c>,
    /// and then <see cref="EndLine"/>.
    /// </summary>
    public const string PrintParents = "print-parents";

    /// <summary>
    /// <c>spawn &lt;command&gt; [arguments]</c>: runs the helper assembly as a child process with the given command, copies
    /// its standard output, and returns its exit code. It makes the current process a parent of known name.
    /// </summary>
    public const string Spawn = "spawn";

    /// <summary>
    /// <c>wait</c>: writes <see cref="ReadyLine"/> and waits until a line arrives on the standard input or the process is
    /// killed.
    /// </summary>
    public const string Wait = "wait";

    public const string AcquiredLine = "acquired";

    public const string ReadyLine = "ready";

    public const string EndLine = "end";
}
