// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using Metalama.Backstage;
using SharpCrafters.Backstage.Application;
using SharpCrafters.Backstage.Extensibility;
using SharpCrafters.Backstage.Licensing;

namespace SharpCrafters.Backstage.PlatformTests;

/// <summary>
/// Creates the Backstage services of the platform tests, as an application that consumes the Metalama.Backstage package
/// creates them. The services are the real ones: the platform tests exist to run the code that the unit tests replace with
/// fakes.
/// </summary>
public static class PlatformTestServices
{
    public static IServiceProvider CreateServiceProvider( bool addLicensing = false, bool addUserInterface = false )
        => BackstageServiceFactory.CreateServiceProvider(
            new BackstageInitializationOptions( new PlatformTestApplicationInfo(), MetalamaProduct.Instance )
            {
                AddLicensing = addLicensing,
                AddUserInterface = addUserInterface,

                // The test authority, so that a test can sign a license key in the current process. The key is still
                // signed and verified by the cryptography library of the platform.
                LicensingOptions = LicensingInitializationOptions.ForTest( _ => { } )
            } );

    private sealed class PlatformTestApplicationInfo() : ApplicationInfoBase( typeof(PlatformTestApplicationInfo).Assembly, MetalamaProduct.Profile )
    {
        public override string Name => "SharpCrafters.Backstage.PlatformTests";

        public override bool IsTelemetryEnabled => false;

        public override bool ShouldCreateLocalCrashReports => false;
    }
}
