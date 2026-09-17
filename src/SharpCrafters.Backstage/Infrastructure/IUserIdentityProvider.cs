// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using JetBrains.Annotations;
using SharpCrafters.Backstage.Extensibility;

namespace SharpCrafters.Backstage.Infrastructure;

/// <summary>
/// Provides the names under which the operating system knows the current user and the current machine.
/// </summary>
/// <remarks>
/// <para>
/// A license server accounts a seat per user and per machine, and receives both names in the query string of its
/// lease request, so both must be substitutable for a test to assert the request that the client sends.
/// </para>
/// <para>
/// This is not <see cref="IMachineIdProvider"/>, which reports a stable <i>identifier</i> of the machine with a
/// specific meaning for the license audit. The machine name is a display name: it changes when the machine is
/// renamed, and it is not suitable for identifying a device.
/// </para>
/// </remarks>
[PublicAPI]
public interface IUserIdentityProvider : IBackstageService
{
    /// <summary>
    /// Gets the name of the account that runs the current process, without a domain on Windows.
    /// </summary>
    string UserName { get; }

    /// <summary>
    /// Gets the name of the machine on which the current process runs.
    /// </summary>
    string MachineName { get; }
}
