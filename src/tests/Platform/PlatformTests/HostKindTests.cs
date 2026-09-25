// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Testing;
using Xunit;

namespace SharpCrafters.Backstage.PlatformTests;

/// <summary>
/// Fails the run when the launcher did not declare the kind of host. Without this test, a missing declaration would only
/// skip the tests that depend on the kind of host, and the run would pass without having run them.
/// </summary>
public sealed class HostKindTests
{
    [Fact]
    public void HostKindIsDeclared()
        => Assert.True(
            PlatformConditions.CurrentHost != null,
            $"The environment variable {PlatformConditions.HostVariableName} must be 'container' or 'host'." );
}
