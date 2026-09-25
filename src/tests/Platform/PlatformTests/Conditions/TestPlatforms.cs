// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

namespace SharpCrafters.Backstage.PlatformTests.Conditions;

/// <summary>
/// The operating systems on which a platform test runs.
/// </summary>
[Flags]
public enum TestPlatforms
{
    Windows = 1,
    Linux = 2,
    MacOS = 4,
    Unix = Linux | MacOS,
    All = Windows | Unix
}
