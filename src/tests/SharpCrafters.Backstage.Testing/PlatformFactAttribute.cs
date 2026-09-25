// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using Xunit;

namespace SharpCrafters.Backstage.Testing;

/// <summary>
/// A test that runs on the given operating systems and kinds of host, and is reported as skipped elsewhere.
/// </summary>
public sealed class PlatformFactAttribute : FactAttribute
{
    public PlatformFactAttribute( TestPlatforms platforms, TestHosts hosts = TestHosts.All )
    {
        this.Skip = PlatformConditions.GetSkipReason( platforms, hosts );
    }
}
