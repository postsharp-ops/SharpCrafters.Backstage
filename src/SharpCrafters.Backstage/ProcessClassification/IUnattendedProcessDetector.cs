// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Extensibility;

namespace SharpCrafters.Backstage.ProcessClassification;

/// <summary>
/// Determines whether the current process runs without a user, for instance on a build server or in a container.
/// </summary>
public interface IUnattendedProcessDetector : IBackstageService
{
    /// <summary>
    /// Detects whether the current process is unattended and stores the answer. It is called once, when the services
    /// are initialized.
    /// </summary>
    /// <remarks>
    /// The detection walks the parent processes. It runs at initialization and not when the answer is first needed,
    /// because a parent process can exit before the current process ends, and a later walk would then find no parent.
    /// </remarks>
    void Initialize();

    /// <summary>
    /// Gets a value indicating whether the current process is unattended.
    /// </summary>
    /// <exception cref="InvalidOperationException"><see cref="Initialize"/> has not been called.</exception>
    bool IsCurrentProcessUnattended { get; }
}