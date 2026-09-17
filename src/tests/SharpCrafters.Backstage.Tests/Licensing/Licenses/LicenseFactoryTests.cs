// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Licensing;
using SharpCrafters.Backstage.Licensing.Consumption;
using SharpCrafters.Backstage.Licensing.Licenses;
using System;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace SharpCrafters.Backstage.Tests.Licensing.Licenses
{
    public sealed class LicenseFactoryTests : LicensingTestsBase
    {
        public LicenseFactoryTests( ITestOutputHelper logger )
            : base( logger ) { }

        private LicenseFactory _licenseFactory = null!;

        private LicenseFactory LicenseFactory
        {
            get
            {
                this.EnsureServicesInitialized();

                return this._licenseFactory;
            }
        }

        protected override void OnAfterServicesCreated( Services services )
        {
            base.OnAfterServicesCreated( services );

            this._licenseFactory = new LicenseFactory( services.ServiceProvider );
            this.UserDeviceDetection.IsInteractiveDevice = true;
        }

        [Fact]
        public void NullLicenseStringFails()
        {
            Assert.False( this.LicenseFactory.TryCreate( "", out _, out _ ) );
        }

        [Fact]
        public void EmptyLicenseStringFails()
        {
            Assert.False( this.LicenseFactory.TryCreate( string.Empty, out _, out _ ) );
        }

        [Fact]
        public void WhitespaceLicenseStringFails()
        {
            Assert.False( this.LicenseFactory.TryCreate( " ", out _, out _ ) );
        }

        [Fact]
        public async Task InvalidLicenseStringCreatesInvalidLicense()
        {
            const string invalidLicenseString = "SomeInvalidLicenseString";
            Assert.True( this.LicenseFactory.TryCreate( invalidLicenseString, out var license, out var errorMessage ) );
            Assert.Null( errorMessage );
            Assert.True( license is License );
            Assert.False( (await license.GetConsumptionPropertiesAsync( LicenseConsumptionOptions.Default )).IsSuccess );
        }

        [Fact]
        public async Task RevokedLicenseStringCreatesInvalidLicense()
        {
            // ReSharper disable StringLiteralTypo
            const string revokedLicenseString =
                "1-ZEQQQQQQZTQEQCRCE4UW3UFEB4URXMHRB8KQBJJSB64LX7EAEJZWKEM8SCXJK6KJLFD92CAJFQKCGC67A9NVYA2JGNEHLB8QQG4JAF94J58KUJQZW8ZQQDTFJJPA";

            // ReSharper restore StringLiteralTypo

            Assert.True( this.LicenseFactory.TryCreate( revokedLicenseString, out var license, out var errorMessage ) );
            Assert.Null( errorMessage );
            Assert.True( license is License );
            Assert.False( (await license.GetConsumptionPropertiesAsync( LicenseConsumptionOptions.Default )).IsSuccess );
        }

        [Fact]
        public async Task ValidLicenseKeyCreatesValidLicense()
        {
            Assert.True( this.LicenseFactory.TryCreate( LicenseKeyProvider.PostSharpUltimate, out var license, out var errorMessage ) );
            Assert.Null( errorMessage );
            Assert.True( license is License );
            var consumptionResult = await license.GetConsumptionPropertiesAsync( LicenseConsumptionOptions.Default );
            Assert.True( consumptionResult.IsSuccess );
            Assert.NotNull( consumptionResult.Properties );
            Assert.Null( consumptionResult.ErrorMessage );
        }

        /// <summary>
        /// Tests that a well-formed license server URL is turned into a leased license and not into a license key, and
        /// that the factory contacts nothing while doing so: the server is reached when the license is consumed or
        /// registered, not when it is created, because this method is called wherever a license string is met.
        /// </summary>
        [Theory]
        [InlineData( "http://license.test" )]
        [InlineData( "https://license.test" )]
        [InlineData( "https://license.test:8443/postsharp" )]
        public void UrlCreatesLeasedLicense( string url )
        {
            Assert.True( this.LicenseFactory.TryCreate( url, out var license, out var errorMessage ) );
            Assert.Null( errorMessage );
            Assert.IsType<LeasedLicense>( license );
            Assert.Empty( this.HttpClientFactory.ProcessedRequests );
        }

        /// <summary>
        /// Tests that a URL which cannot be a license server is refused with the reason, rather than being left to
        /// fail later as an unparsable license key.
        /// </summary>
        [Theory]
        [InlineData( "https://license.test?user=x", "query string" )]
        [InlineData( "ftp://license.test", "HTTP and HTTPS" )]
        [InlineData( "file:///c:/licenses", "HTTP and HTTPS" )]
        [InlineData( "https://alice:secret@license.test", "user name" )]
        public void MalformedUrlIsRefusedWithItsReason( string url, string expectedMessageSubstring )
        {
            Assert.False( this.LicenseFactory.TryCreate( url, out var license, out var errorMessage ) );
            Assert.Null( license );
            Assert.Contains( expectedMessageSubstring, errorMessage, StringComparison.Ordinal );
        }

        /// <summary>
        /// Tests that a license key whose signature is invalid is rejected, whichever key of the authority it claims
        /// to be signed with.
        /// </summary>
        /// <param name="signatureKeyId">The identifier of the key that the license key claims to be signed with.</param>
        [Theory]
        [InlineData( TestLicensingAuthorityProvider.DsaTestKeyId )]
        [InlineData( TestLicensingAuthorityProvider.ECDsaTestKeyId )]
        public async Task LicenseKeyWithInvalidSignatureFails( byte signatureKeyId )
        {
            var licenseKey = new LicenseKeyDataBuilder
            {
                Product = LicenseProduct.MetalamaProfessional, Signature = new byte[16], SignatureKeyId = signatureKeyId
            }.SerializeToLicenseString();

            Assert.True( this.LicenseFactory.TryCreate( licenseKey, out var license, out var errorMessage ) );
            Assert.Null( errorMessage );
            Assert.True( license is License );

            var consumptionResult = await license.GetConsumptionPropertiesAsync( LicenseConsumptionOptions.Default );
            Assert.False( consumptionResult.IsSuccess );
            Assert.Null( consumptionResult.Properties );
            Assert.NotEmpty( consumptionResult.ErrorMessage );
        }
    }
}
