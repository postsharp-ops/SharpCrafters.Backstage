// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using JetBrains.Annotations;
using SharpCrafters.Backstage.Infrastructure;

namespace SharpCrafters.Backstage.Testing;

/// <summary>
/// Reports a user name and a machine name chosen by the test instead of those of the machine that runs the test, so
/// that a test can pin the request that a license server receives and the seat that it accounts.
/// </summary>
[PublicAPI]
public sealed class TestUserIdentityProvider : IUserIdentityProvider
{
    /// <summary>
    /// The user name reported unless the test sets <see cref="UserName"/>.
    /// </summary>
    public const string DefaultUserName = "testuser";

    /// <summary>
    /// The machine name reported unless the test sets <see cref="MachineName"/>.
    /// </summary>
    public const string DefaultMachineName = "TEST-MACHINE";

    public string UserName { get; set; } = DefaultUserName;

    public string MachineName { get; set; } = DefaultMachineName;
}
