// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using Microsoft.Extensions.DependencyInjection;
using SharpCrafters.Backstage.Application;
using SharpCrafters.Backstage.Extensibility;
using SharpCrafters.Backstage.Licensing;
using SharpCrafters.Backstage.Licensing.Consumption;
using SharpCrafters.Backstage.Licensing.Consumption.Sources;
using SharpCrafters.Backstage.ProcessClassification;
using SharpCrafters.Backstage.Testing;
using SharpCrafters.Backstage.Tests.Extensibility;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace SharpCrafters.Backstage.Tests.Licensing.Consumption;

public sealed class LicenseSourcePriorityTests : LicensingTestsBase
{
    private static string ProjectLicense => LicenseKeyProvider.MetalamaProfessionalBusiness;

    private static string UserLicense => LicenseKeyProvider.MetalamaProfessionalPersonal;

    public LicenseSourcePriorityTests( ITestOutputHelper logger ) : base( logger ) { }

    protected override void ConfigureServices( ServiceProviderBuilder services ) { }

    private async Task<ILicenseConsumer> CreateLicenseConsumerAsync(
        bool isUnattendedProcess,
        string? projectLicense,
        string? userLicense,
        bool isPreview,
        Action<LicensingMessage>? reportMessage = null )
    {
        var serviceCollection = this.CloneServiceCollection();

        var serviceProviderBuilder = new ServiceCollectionBuilder( serviceCollection );

        serviceProviderBuilder.AddSingleton<IApplicationInfoProvider>(
            new ApplicationInfoProvider(
                new TestApplicationInfo( "License Source Priority Test App", isPreview, "1.0.0", new DateTime( 2022, 1, 1, 0, 0, 0, DateTimeKind.Utc ) ) ) );

        serviceProviderBuilder.AddSingleton<IUnattendedProcessDetector>(
            new TestUnattendedProcessDetector() { IsCurrentProcessUnattended = isUnattendedProcess } );

        serviceProviderBuilder.AddSingleton<ILicenseConsumptionService>(
            sp =>
            {
                var licenseSources = new List<ILicenseSource> { new UnattendedLicenseSource( sp ), new UserProfileLicenseSource( sp ) };

                return new LicenseConsumptionService( sp, licenseSources );
            } );

        var serviceProvider = serviceCollection.BuildServiceProvider();

        if ( userLicense != null )
        {
            Assert.True( (await this.LicenseRegistrationService.RegisterLicenseAsync( userLicense )).IsSuccess );
        }

        var service = serviceProvider.GetRequiredBackstageService<ILicenseConsumptionService>();

        return await service.CreateConsumerAsync( new LicenseConsumptionOptions { ProjectLicenseKey = projectLicense }, reportMessage );
    }

    [Fact]
    public async Task NoMessageGivenWithNoLicense()
    {
        var hasMessage = false;
        await this.CreateLicenseConsumerAsync( false, null, null, false, _ => hasMessage = true );
        Assert.False( hasMessage );

        // Note that trying to consume does report a message in this case.
    }

    [Fact]
    public async Task UnattendedLicenseHasHighestPriority()
    {
        var licenseConsumptionManager = await this.CreateLicenseConsumerAsync( true, null, UserLicense, false );

        Assert.True(
            licenseConsumptionManager.TryConsume( new DelegateLicenseRequirement( context => context.License.LicenseType == LicenseType.Unattended ) ) );
    }

    [Fact]
    public async Task ProjectLicenseHasPriorityOverUserLicense()
    {
        var licenseConsumptionManager = await this.CreateLicenseConsumerAsync( false, ProjectLicense, UserLicense, false );
        Assert.True( licenseConsumptionManager.TryConsume( new DelegateLicenseRequirement( context => context.License.LicenseString == ProjectLicense ) ) );
    }
}