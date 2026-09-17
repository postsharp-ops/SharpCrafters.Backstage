// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Licensing.Consumption.Sources;
using SharpCrafters.Backstage.Testing;
using System.IO.Abstractions.TestingHelpers;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace SharpCrafters.Backstage.Tests.Licensing.LicenseSources
{
    public sealed class UserProfileLicenseSourceTests : LicensingTestsBase
    {
        private const string _licenseFilePath = "licensing.json";

        public UserProfileLicenseSourceTests( ITestOutputHelper logger )
            : base( logger ) { }

        [Fact]
        public async Task NonexistentFileIsReported()
        {
            UserProfileLicenseSource source = new( this.ServiceProvider );

            Assert.Empty( await source.GetLicensesAsync( _ => { } ).DrainAsync() );
        }

        [Fact]
        public async Task EmptyFilePasses()
        {
            this.FileSystem.Mock.AddFile( _licenseFilePath, new MockFileData( "" ) );

            UserProfileLicenseSource source = new( this.ServiceProvider );

            Assert.Empty( await source.GetLicensesAsync( _ => { } ).DrainAsync() );
        }
    }
}
