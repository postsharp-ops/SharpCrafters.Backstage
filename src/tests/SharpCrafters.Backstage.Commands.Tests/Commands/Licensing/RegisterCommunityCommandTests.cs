// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Configuration;
using SharpCrafters.Backstage.Licensing;
using System.Threading.Tasks;
using Xunit;

namespace SharpCrafters.Backstage.Commands.Tests.Commands.Licensing
{
    public sealed class RegisterCommunityCommandTests : LicensingCommandsTestsBase
    {
        public RegisterCommunityCommandTests( ITestOutputHelper logger )
            : base( logger ) { }

        private CommunityLicenseReason StoredReason => this.ConfigurationManager!.Get<LicensingConfiguration>().CommunityLicenseReason;

        [Fact]
        public async Task TheAnswerOnTheStandardInputIsTheReason()
        {
            await this.TestCommandAsync( "license community", "You are now using Metalama Community.", input: "1\n" );

            await this.TestCommandAsync( "license list", "Metalama Community" );
            Assert.Equal( CommunityLicenseReason.Individual, this.StoredReason );
        }

        [Fact]
        public async Task TheCommandFailsWhenNoInputIsAvailable()
        {
            await this.TestCommandAsync( "license community", "use the --reason option", expectedExitCode: 1 );

            await this.TestCommandAsync( "license list", unexpectedOutput: "Metalama Community" );
            Assert.Equal( CommunityLicenseReason.None, this.StoredReason );
        }

        [Fact]
        public async Task AnInvalidAnswerIsAskedAgain()
        {
            await this.TestCommandAsync( "license community", "'x' is not a number from 1 to 4.", input: "x\n3\n" );

            Assert.Equal( CommunityLicenseReason.OpenSource, this.StoredReason );
        }

        [Fact]
        public async Task TheReasonOptionRegistersWithoutAsking()
        {
            await this.TestCommandAsync( "license community --reason SmallBusiness", unexpectedOutput: CommunityLicenseReasonExtensions.Question );

            Assert.Equal( CommunityLicenseReason.SmallBusiness, this.StoredReason );
        }

        [Fact]
        public async Task TheReasonNoneIsRefused()
        {
            await this.TestCommandAsync( "license community --reason None", "'None' is not a valid reason.", expectedExitCode: 1 );

            Assert.Equal( CommunityLicenseReason.None, this.StoredReason );
        }

        [Fact]
        public async Task AnUnknownReasonIsRefused()
        {
            await this.TestCommandAsync( "license community --reason Bogus", expectedExitCode: -1 );

            Assert.Equal( CommunityLicenseReason.None, this.StoredReason );
        }
    }
}
