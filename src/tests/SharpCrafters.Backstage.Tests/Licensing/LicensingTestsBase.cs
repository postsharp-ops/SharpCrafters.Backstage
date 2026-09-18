// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using Metalama.Backstage;
using SharpCrafters.Backstage.Application;
using SharpCrafters.Backstage.Extensibility;
using SharpCrafters.Backstage.Testing;
using System;
using Xunit.Abstractions;

namespace SharpCrafters.Backstage.Tests.Licensing
{
    public abstract class LicensingTestsBase : TestsBase
    {
        protected override void OnAfterServicesCreated( Services services )
        {
            base.OnAfterServicesCreated( services );
            this.UserDeviceDetection.IsInteractiveDevice = true;
        }

        protected static TestLicenseKeyProvider LicenseKeyProvider { get; } = new();

        /// <summary>
        /// Gets the version of the test application. A group of license keys whose minimal version is greater than
        /// this version is not read by the services under test.
        /// </summary>
        protected Version CurrentVersion => this.ApplicationInfo.GetLicensingVersion();

        /// <param name="product">
        /// The product family under test. It decides which license keys are consumable at all, because a catalog
        /// rejects a key of another family before any requirement is consulted. The default is Metalama.
        /// </param>
        private protected LicensingTestsBase( ITestOutputHelper logger, bool isTelemetryEnabled = false, BackstageProduct? product = null ) : base(
            logger,
            new BackstageInitializationOptions(
                new TestApplicationInfo(
                    "Licensing Test App",
                    false,
                    "1.0",
                    new DateTime( 2021, 1, 1, 0, 0, 0, DateTimeKind.Utc ) ) { IsTelemetryEnabled = isTelemetryEnabled },
                product ?? MetalamaProduct.Instance ) { AutoUploadTelemetry = false } ) { }
    }
}