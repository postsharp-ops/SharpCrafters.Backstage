// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

namespace SharpCrafters.Backstage.PlatformTests.Conditions;

/// <summary>
/// The kinds of host on which a platform test runs. The kind is declared by the launcher of the tests through
/// <see cref="PlatformConditions.HostVariableName"/>, because detecting it is part of the code under test.
/// </summary>
[Flags]
public enum TestHosts
{
    Container = 1,
    Host = 2,
    All = Container | Host
}
