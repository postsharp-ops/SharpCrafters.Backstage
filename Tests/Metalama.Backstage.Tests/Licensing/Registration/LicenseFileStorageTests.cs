// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using Metalama.Backstage.Configuration;
using Metalama.Backstage.Extensibility;
using Metalama.Backstage.Licensing.Registration;
using Xunit;
using Xunit.Abstractions;

namespace Metalama.Backstage.Tests.Licensing.Registration
{
    public sealed class LicenseFileStorageTests : LicensingTestsBase
    {
        public LicenseFileStorageTests( ITestOutputHelper logger )
            : base( logger ) { }

        protected override void ConfigureServices( ServiceProviderBuilder services )
        {
            base.ConfigureServices( services );
            services.AddSingleton<IConfigurationManager>( serviceProvider => new Configuration.ConfigurationManager( serviceProvider ) );
        }

        private LicensingConfigurationModel OpenOrCreateStorage()
        {
            return LicensingConfigurationModel.Create( this.ServiceProvider );
        }

        private void AssertFileContainsGen0( string? expectedLicenseString )
        {
            Assert.Equal( expectedLicenseString, this.ReadStoredGen0LicenseString() );
        }

        private void AssertFileContainsGen1( string? expectedLicenseString )
        {
            Assert.Equal( expectedLicenseString, this.ReadStoredGen1LicenseString() );
        }

        private void AssertStorageContains( LicensingConfigurationModel storage, string? expectedLicenseString )
        {
            Assert.Equal( expectedLicenseString, storage.LicenseString );

            if ( expectedLicenseString == null )
            {
                Assert.Null( storage.LicenseProperties );
            }
            else
            {
                if ( !this.LicenseFactory.TryCreate( expectedLicenseString, out var expectedLicense, out _ ) )
                {
                    Assert.Null( storage.LicenseProperties );

                    return;
                }

                if ( !expectedLicense.TryGetProperties( out var expectedData, out _ ) )
                {
                    Assert.Null( storage.LicenseProperties );

                    return;
                }

                Assert.Equal( expectedData, storage.LicenseProperties );
            }
        }

        private void Add( LicensingConfigurationModel storage, string licenseString )
        {
            var data = this.GetLicenseRegistrationData( licenseString );
            storage.SetLicense( licenseString, data );
        }

        [Fact]
        public void ExistingStorageSucceedsToRead()
        {
            this.SetStoredGen0LicenseString( "dummy" );
            this.AssertFileContainsGen0( "dummy" );
        }

        [Fact]
        public void NonExistentStorageCanBeRead()
        {
            var storage = this.OpenOrCreateStorage();
            this.AssertStorageContains( storage, null );
        }

        [Fact]
        public void EmptyStorageCanBeCreated()
        {
            this.OpenOrCreateStorage();
            this.AssertFileContainsGen0( null );
        }

        [Fact]
        public void NonEmptyStorageCanBeCreated()
        {
            var storage = this.OpenOrCreateStorage();
            this.Add( storage, LicenseKeyProvider.PostSharpUltimate );

            this.AssertFileContainsGen1( LicenseKeyProvider.PostSharpUltimate );
        }

        [Fact]
        public void ValidLicenseKeyCanBeRetrieved()
        {
            this.SetStoredGen0LicenseString( LicenseKeyProvider.PostSharpUltimate );

            var storage = this.OpenOrCreateStorage();

            this.AssertStorageContains( storage, LicenseKeyProvider.PostSharpUltimate );
        }

        [Fact]
        public void InvalidLicenseKeyCanBeRetrieved()
        {
            this.SetStoredGen0LicenseString( "dummy" );

            var storage = this.OpenOrCreateStorage();

            this.AssertStorageContains( storage, "dummy" );
            this.AssertFileContainsGen0( "dummy" );
        }

        [Fact]
        public void PreviousValidLicenseKeysAreReplaced()
        {
            this.SetStoredGen1LicenseString( LicenseKeyProvider.PostSharpUltimate );

            var storage = this.OpenOrCreateStorage();
            this.Add( storage, LicenseKeyProvider.MetalamaProfessionalPersonal );

            this.AssertStorageContains( storage, LicenseKeyProvider.MetalamaProfessionalPersonal );
            this.AssertFileContainsGen1( LicenseKeyProvider.MetalamaProfessionalPersonal );
        }

        [Fact]
        public void PreviousInvalidLicenseKeyIsNotReplacedWithGen1()
        {
            this.SetStoredGen0LicenseString( "dummy" );

            var storage = this.OpenOrCreateStorage();
            var communityLicense = LicenseKeyProvider.MetalamaCommunity;
            this.Add( storage, communityLicense );

            this.AssertStorageContains( storage, communityLicense );
            this.AssertFileContainsGen0( "dummy" );
            this.AssertFileContainsGen1( communityLicense );
        }

        [Fact]
        public void PreviousValidLicenseKeysAreNotReplacedWithGen1()
        {
            this.SetStoredGen0LicenseString( LicenseKeyProvider.PostSharpUltimate );

            var storage = this.OpenOrCreateStorage();
            var communityLicense = LicenseKeyProvider.MetalamaCommunity;
            this.Add( storage, communityLicense );

            this.AssertStorageContains( storage, communityLicense );
            this.AssertFileContainsGen0( LicenseKeyProvider.PostSharpUltimate );
            this.AssertFileContainsGen1( communityLicense );
        }

        [Fact]
        public void Gen1LiceseKeyTakesPriorityOverGen0()
        {
            this.SetStoredGen0LicenseString( LicenseKeyProvider.PostSharpUltimate );
            var freeLicense = LicenseKeyProvider.MetalamaCommunity;
            this.SetStoredGen1LicenseString( freeLicense );
        }
    }
}