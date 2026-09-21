// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using JetBrains.Annotations;
using SharpCrafters.Backstage.Extensibility;
using SharpCrafters.Backstage.Infrastructure.ProcessClassification;
using System.Runtime.InteropServices;

// ReSharper disable InconsistentNaming

namespace SharpCrafters.Backstage.Infrastructure;

/// <summary>
/// Provides runtime information about the current platform and process.
/// </summary>
[PublicAPI]
public interface IRuntimeInformation : IBackstageService
{
    /// <summary>
    /// Gets the platform on which the application is running.
    /// </summary>
    /// <param name="osPlatform">The platform to check.</param>
    /// <returns>True if the application is running on the specified platform; otherwise, false.</returns>
    bool IsOSPlatform( OSPlatform osPlatform );

    /// <summary>
    /// Gets the architecture of the running process.
    /// </summary>
    Architecture ProcessArchitecture { get; }

    /// <summary>
    /// Gets the architecture of the operating system.
    /// </summary>

    Architecture OSArchitecture { get; }

    /// <summary>
    /// Gets the kind of the current process (e.g. <see cref="ProcessClassification.ProcessKind.Rider"/>),
    /// abstracted so a fake can simulate a host without depending on the real process name.
    /// </summary>
    ProcessKind ProcessKind { get; }

    /// <summary>
    /// Gets a value indicating whether the current process runs inside a container.
    /// </summary>
    /// <remarks>
    /// A container is one of the reasons why a process is unattended, and
    /// <c>IApplicationInfo.IsUnattendedProcess</c> is the question most
    /// callers have. This one is here because a caller may need the container itself: a Windows container has no
    /// just-in-time debugger, whether or not somebody is watching the build.
    /// </remarks>
    bool IsRunningInContainer { get; }
}