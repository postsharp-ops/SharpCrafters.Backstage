// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using JetBrains.Annotations;
using SharpCrafters.Backstage.Extensibility;

namespace SharpCrafters.Backstage.Profiling;

/// <summary>
/// Registers the profiling feature, which the core services no longer register themselves.
/// </summary>
/// <remarks>
/// The feature reads the <c>profiling</c> section of <c>diagnostics.json</c>, which the configuration of the
/// diagnostics still declares whether or not this package is present: the setting is part of the file format, and a
/// product that does not register the service simply ignores it. <see cref="BackstageServicesInitializer"/> resolves
/// <see cref="IProfilingService"/> optionally and does nothing when it is absent, so a product opts in by calling
/// this method and by nothing else.
/// </remarks>
[PublicAPI]
public static class BackstageProfilingExtensions
{
    public static ServiceProviderBuilder AddProfiling( this ServiceProviderBuilder builder )
    {
        builder.AddService( typeof(IProfilingService), serviceProvider => new ProfilingService( serviceProvider ) );

        return builder;
    }
}
