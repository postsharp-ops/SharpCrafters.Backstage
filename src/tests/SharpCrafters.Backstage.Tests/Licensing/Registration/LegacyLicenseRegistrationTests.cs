// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Configuration;
using SharpCrafters.Backstage.Licensing;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace SharpCrafters.Backstage.Tests.Licensing.Registration;

#pragma warning disable CS0612, CS0618 // Type or member is obsolete

public sealed class LegacyLicenseRegistrationTests : LicensingTestsBase
{
    public LegacyLicenseRegistrationTests( ITestOutputHelper logger ) : base( logger ) { }

    [Fact]
    public async Task FreeLicenseIsNotOverwrittenByCommunity()
    {
        Assert.True( (await this.LicenseRegistrationService.RegisterLicenseAsync( LicenseKeyProvider.MetalamaFree )).IsSuccess );
        Assert.Single( this.LicenseRegistrationService.RegisteredLicenses );
        Assert.True( (await this.LicenseRegistrationService.RegisterLicenseAsync( LicenseKeyProvider.MetalamaCommunity )).IsSuccess );

        var registeredLicenses = this.LicenseRegistrationService.RegisteredLicenses.ToList();
        Assert.Equal( 2, registeredLicenses.Count );
        Assert.Contains( registeredLicenses, l => l.Product == LicenseProduct.MetalamaFree );
        Assert.Contains( registeredLicenses, l => l.Product == LicenseProduct.MetalamaCommunity );
    }

    [Fact]
    public async Task CommunityLicenseIsNotOverwrittenByFree()
    {
        Assert.True( (await this.LicenseRegistrationService.RegisterLicenseAsync( LicenseKeyProvider.MetalamaCommunity )).IsSuccess );
        Assert.Single( this.LicenseRegistrationService.RegisteredLicenses );
        Assert.True( (await this.LicenseRegistrationService.RegisterLicenseAsync( LicenseKeyProvider.MetalamaFree )).IsSuccess );

        var registeredLicenses = this.LicenseRegistrationService.RegisteredLicenses.ToList();
        Assert.Equal( 2, registeredLicenses.Count );
        Assert.Contains( registeredLicenses, l => l.Product == LicenseProduct.MetalamaFree );
        Assert.Contains( registeredLicenses, l => l.Product == LicenseProduct.MetalamaCommunity );
    }

    [Fact]
    public async Task MetalamaCommunityNotStoredInLegacyField()
    {
        Assert.True( (await this.LicenseRegistrationService.RegisterLicenseAsync( LicenseKeyProvider.MetalamaCommunity )).IsSuccess );

        var configuration = this.ConfigurationManager!.Get<LicensingConfiguration>();

        Assert.Null( configuration.LegacyLicense );
        Assert.Single( configuration.Licenses );
        Assert.Contains( LicenseKeyProvider.MetalamaCommunity, configuration.Licenses );
    }

    [Fact]
    public async Task MetalamaProfessionalStoredInLegacyField()
    {
        Assert.True( (await this.LicenseRegistrationService.RegisterLicenseAsync( LicenseKeyProvider.MetalamaProfessionalBusiness )).IsSuccess );

        var configuration = this.ConfigurationManager!.Get<LicensingConfiguration>();

        Assert.Equal( LicenseKeyProvider.MetalamaProfessionalBusiness, configuration.LegacyLicense );
    }
}
