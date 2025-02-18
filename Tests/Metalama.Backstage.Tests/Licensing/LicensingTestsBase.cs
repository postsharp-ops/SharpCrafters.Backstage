// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using Metalama.Backstage.Licensing.Licenses;
using Metalama.Backstage.Licensing.Registration;
using Metalama.Backstage.Testing;
using System;
using Xunit;
using Xunit.Abstractions;

namespace Metalama.Backstage.Tests.Licensing
{
    public abstract class LicensingTestsBase : TestsBase
    {
        private LicenseFactory _licenseFactory = null!;

        private protected LicenseFactory LicenseFactory
        {
            get
            {
                this.EnsureServicesInitialized();

                return this._licenseFactory;
            }
        }

        protected static TestLicenseKeyProvider LicenseKeyProvider => BackstageTestLicenseKeyProvider.LicenseKeyProvider;

        private protected LicensingTestsBase( ITestOutputHelper logger, bool isTelemetryEnabled = false ) : base(
            logger,
            new TestApplicationInfo(
                "Licensing Test App",
                false,
                "1.0",
                new DateTime( 2021, 1, 1, 0, 0, 0, DateTimeKind.Utc ) ) { IsTelemetryEnabled = isTelemetryEnabled } ) { }

        protected override void OnAfterServicesCreated( Services services )
        {
            base.OnAfterServicesCreated( services );

            this._licenseFactory = new LicenseFactory( services.ServiceProvider );
            this.UserDeviceDetection.IsInteractiveDevice = true;
        }

        protected string? ReadStoredGen0LicenseString() => TestLicensingConfigurationHelpers.ReadStoredGen0LicenseString( this.ServiceProvider );

        protected string? ReadStoredGen1LicenseString() => TestLicensingConfigurationHelpers.ReadStoredGen1LicenseString( this.ServiceProvider );

        protected void SetStoredGen0LicenseString( string licenseString )
            => TestLicensingConfigurationHelpers.SetStoredGen0LicenseString( this.ServiceProvider, licenseString );

        protected void SetStoredGen1LicenseString( string licenseString )
            => TestLicensingConfigurationHelpers.SetStoredGen1LicenseString( this.ServiceProvider, licenseString );

        internal LicenseProperties GetLicenseRegistrationData( string licenseString )
        {
            Assert.True( this.LicenseFactory.TryCreate( licenseString, out var license, out var errorMessage ) );
            Assert.Null( errorMessage );
            Assert.True( license.TryGetProperties( out var data, out errorMessage ) );
            Assert.Null( errorMessage );

            return data;
        }
    }
}