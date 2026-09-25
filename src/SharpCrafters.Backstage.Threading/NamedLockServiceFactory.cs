// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Common.Testing.Hooks;
using System.Runtime.InteropServices;

namespace SharpCrafters.Backstage.Threading;

/// <summary>
/// Creates the implementation of <see cref="NamedLockService"/> for the current operating system: one for Windows, and
/// one for Linux and macOS.
/// </summary>
public static class NamedLockServiceFactory
{
    /// <summary>
    /// Creates a named lock service from a service provider.
    /// </summary>
    /// <param name="serviceProvider">
    /// The service provider. It must provide the <see cref="INamedLockServiceEnvironment"/>. The test synchronization
    /// points and the test faults are resolved from it when they are registered, which is never the case in production.
    /// </param>
    public static NamedLockService Create( IServiceProvider serviceProvider )
        => IsWindows ? new WindowsNamedLockService( serviceProvider ) : new UnixNamedLockService( serviceProvider );

    /// <summary>
    /// Creates a named lock service without a service provider, for a component that starts no service, such as an
    /// MSBuild task.
    /// </summary>
    /// <param name="globalLockNamePrefix">
    /// The prefix of the names of the machine-wide locks of the product, for instance <c>Global\Metalama_</c>. It is
    /// the value of <see cref="INamedLockServiceEnvironment.GlobalLockNamePrefix"/> for the product.
    /// </param>
    /// <param name="testSynchronizationProvider">The test synchronization points, or <see langword="null"/> in production.</param>
    /// <param name="testFaultInjector">The test faults, or <see langword="null"/> in production.</param>
    public static NamedLockService Create(
        string globalLockNamePrefix,
        ITestSynchronizationProvider? testSynchronizationProvider = null,
        ITestFaultInjector? testFaultInjector = null )
        => IsWindows
            ? new WindowsNamedLockService( globalLockNamePrefix, testSynchronizationProvider, testFaultInjector )
            : new UnixNamedLockService( globalLockNamePrefix, testSynchronizationProvider, testFaultInjector );

    private static bool IsWindows => RuntimeInformation.IsOSPlatform( OSPlatform.Windows );
}
