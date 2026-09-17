// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using JetBrains.Annotations;
using SharpCrafters.Backstage.Extensibility;
using SharpCrafters.Backstage.UserInterface;

namespace SharpCrafters.Backstage.Testing;

/// <summary>
/// Registers the fakes of the services whose interface the product keeps internal.
/// </summary>
/// <remarks>
/// A process that runs the product from outside the test projects — the license server load simulator is one — can
/// construct such a fake, because the fake itself is public, but cannot name the interface in order to register it.
/// This assembly can, because the product makes its internals visible to it. A test project needs none of this and
/// registers the fake directly.
/// </remarks>
[PublicAPI]
public static class RegisterTestServices
{
    /// <summary>
    /// Registers a fake detection of the device, so that the caller decides whether the session is interactive rather
    /// than the machine that happens to run it.
    /// </summary>
    /// <param name="serviceProviderBuilder">The builder. Call this after the services of the product, whose
    /// registration of the real detection this one replaces.</param>
    /// <param name="service">The fake.</param>
    public static ServiceProviderBuilder AddUserDeviceDetection(
        this ServiceProviderBuilder serviceProviderBuilder,
        TestUserDeviceDetectionService service )
    {
        serviceProviderBuilder.AddService( typeof(IUserDeviceDetectionService), service );

        return serviceProviderBuilder;
    }
}
