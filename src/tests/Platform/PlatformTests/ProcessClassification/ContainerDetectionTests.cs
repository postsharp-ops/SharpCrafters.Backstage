// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Testing;
using Xunit;
using ContainerDetection = SharpCrafters.Backstage.ProcessClassification.ContainerDetection;

namespace SharpCrafters.Backstage.PlatformTests.ProcessClassification;

/// <summary>
/// Compares <see cref="ContainerDetection"/> with the kind of host that the launcher declares. The detection reads the
/// control group and the environment of the first process on Linux, and the account on Windows, which no fake reproduces.
/// </summary>
public sealed class ContainerDetectionTests
{
    [PlatformFact( TestPlatforms.All )]
    public void TheDetectionMatchesTheDeclaredHost()
    {
        Assert.NotNull( PlatformConditions.CurrentHost );

        Assert.Equal( PlatformConditions.CurrentHost == TestHosts.Container, ContainerDetection.IsRunningInContainer() );
    }
}
