// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

namespace SharpCrafters.Backstage.PlatformTests.Conditions;

/// <summary>
/// Decides whether a platform test applies to the current environment.
/// </summary>
/// <remarks>
/// The operating system comes from <see cref="OperatingSystem"/>, which the runtime answers. It does not come from the
/// <c>IRuntimeInformation</c> service of the product, which is part of the code under test.
/// </remarks>
public static class PlatformConditions
{
    /// <summary>
    /// The name of the environment variable that declares the kind of host: <c>container</c> or <c>host</c>.
    /// </summary>
    public const string HostVariableName = "BACKSTAGE_PLATFORM_TEST_HOST";

    public static TestPlatforms? CurrentPlatform { get; } =
        OperatingSystem.IsWindows() ? TestPlatforms.Windows
        : OperatingSystem.IsLinux() ? TestPlatforms.Linux
        : OperatingSystem.IsMacOS() ? TestPlatforms.MacOS
        : null;

    public static TestHosts? CurrentHost { get; } = Environment.GetEnvironmentVariable( HostVariableName ) switch
    {
        "container" => TestHosts.Container,
        "host" => TestHosts.Host,
        _ => null
    };

    /// <summary>
    /// Returns the reason why a test that runs on <paramref name="platforms"/> and <paramref name="hosts"/> is skipped in
    /// the current environment, or <see langword="null"/> when it runs.
    /// </summary>
    public static string? GetSkipReason( TestPlatforms platforms, TestHosts hosts )
    {
        if ( CurrentPlatform is not { } platform || (platforms & platform) == 0 )
        {
            return $"Runs on {platforms}; this is {CurrentPlatform?.ToString() ?? "an unknown operating system"}.";
        }

        if ( hosts != TestHosts.All )
        {
            if ( CurrentHost is not { } host )
            {
                return $"Runs on {hosts}; the variable {HostVariableName} does not declare the kind of host.";
            }

            if ( (hosts & host) == 0 )
            {
                return $"Runs on {hosts}; this is {host}.";
            }
        }

        return null;
    }
}
