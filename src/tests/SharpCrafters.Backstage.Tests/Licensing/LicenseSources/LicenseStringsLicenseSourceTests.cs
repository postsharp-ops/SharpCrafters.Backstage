// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Licensing.Consumption;
using SharpCrafters.Backstage.Licensing.Consumption.Sources;
using SharpCrafters.Backstage.Testing;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace SharpCrafters.Backstage.Tests.Licensing.LicenseSources
{
    public sealed class LicenseStringsLicenseSourceTests : LicensingTestsBase
    {
        public LicenseStringsLicenseSourceTests( ITestOutputHelper logger )
            : base( logger ) { }

        [Fact]
        public async Task OneLicenseStringPasses()
        {
            ExplicitLicenseSource source = new( LicenseKeyProvider.MetalamaProfessionalBusiness, LicenseSourceKind.Test, this.ServiceProvider );

            var license = await source.GetLicensesAsync( _ => { } ).DrainSingleAsync();
            Assert.NotNull( license );

            var consumptionResult = await license.GetConsumptionPropertiesAsync( LicenseConsumptionOptions.Default );
            Assert.True( consumptionResult.IsSuccess );
            Assert.Null( consumptionResult.ErrorMessage );
            var data = consumptionResult.Properties;
            Assert.Equal( LicenseKeyProvider.MetalamaProfessionalBusiness, data!.LicenseString );
        }

        /// <summary>
        /// Verifies that a malformed license string is reported as an invalid license key instead of throwing. A
        /// continuous integration build usually reads the license string from a secret, so a mistyped value is a
        /// likely mistake and must be reported as such. See issue #1859.
        /// </summary>
        [Fact]
        public async Task MalformedLicenseStringIsReportedAsInvalid()
        {
            ExplicitLicenseSource source = new( "NOT-A-REAL-KEY", LicenseSourceKind.Test, this.ServiceProvider );

            var messages = new List<LicensingMessage>();
            var licenses = await source.GetLicensesAsync( messages.Add ).DrainAsync();

            Assert.Empty( licenses );

            var message = Assert.Single( messages );
            Assert.False( message.IsError );
            Assert.Contains( "is invalid", message.Text, StringComparison.Ordinal );
        }
    }
}
