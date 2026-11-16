// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

namespace SharpCrafters.Aspects.Introspection.Query.Projects;

/// <summary>
/// The result of <see cref="DotNetSdkLocator.EnsureRegistered"/>: the SDK whose MSBuild the process uses, or why there is none.
/// </summary>
/// <param name="Sdk">The SDK, or <c>null</c> when none could be registered.</param>
/// <param name="Failure">Why no SDK could be registered, or <c>null</c>.</param>
public sealed record DotNetSdkRegistration( DotNetSdk? Sdk, string? Failure )
{
    /// <summary>
    /// Gets a value indicating whether .NET SDKs are installed but none of them can be loaded by the process, because of their major
    /// version or of their processor architecture. A host can then advise the user to run the process on another runtime or
    /// architecture.
    /// </summary>
    public bool HasIncompatibleSdks { get; init; }
}
