// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using Microsoft.Extensions.DependencyInjection;
using SharpCrafters.Backstage.Application;
using SharpCrafters.Backstage.Licensing.Consumption;
using SharpCrafters.Backstage.Licensing.Consumption.Sources;
using SharpCrafters.Backstage.Testing;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace SharpCrafters.Backstage.Tests.Licensing.Consumption
{
    public sealed class SubscriptionValidationTests : LicensingTestsBase
    {
        private static readonly TimeSpan _subscriptionGracePeriod = TimeSpan.FromDays( 30 );

        public SubscriptionValidationTests( ITestOutputHelper logger )
            : base( logger ) { }

        private static IApplicationInfo CreateApplicationInfo( DateTime buildDate, params IComponentInfo[] components )
            => new TestApplicationInfo(
                $"Subscription Validation Test App built {buildDate:d}",
                false,
                $"<ver-{buildDate:d}>",
                buildDate ) { Components = components.ToImmutableArray() };

        private static IComponentInfo CreateComponentInfo( DateTime buildDate, bool isThirdParty )
            => new TestComponentInfo(
                $"Subscription Validation Test Component built {buildDate:d} {(isThirdParty ? "not by us" : "by us")}",
                $"<ver-{buildDate:d}>",
                false,
                buildDate,
                isThirdParty ? "The Corp" : "PostSharp Technologies" );

        private Task AssertPassesAsync( IApplicationInfo applicationInfo, string? licenseKey = null ) => this.TestCore( applicationInfo, true, null, licenseKey );

        private Task AssertFailsAsync(
            IApplicationInfo applicationInfo,
            IComponentInfo? infringingComponent = null,
            string? licenseKey = null )
            => this.TestCore( applicationInfo, false, infringingComponent, licenseKey );

        private async Task TestCore(
            IApplicationInfo applicationInfo,
            bool mustSucceed,
            IComponentInfo? infringingComponent,
            string? licenseKey )
        {
            licenseKey ??= LicenseKeyProvider.MetalamaProfessionalBusiness;
            var serviceCollection = this.CloneServiceCollection();
            serviceCollection.AddSingleton<IApplicationInfoProvider>( new ApplicationInfoProvider( applicationInfo ) );
            var serviceProvider = serviceCollection.BuildServiceProvider();

            var messages = new List<LicensingMessage>();

            var licenseConsumingService = new LicenseConsumptionService(
                serviceProvider,
                [new ExplicitLicenseSource( licenseKey, LicenseSourceKind.Test, serviceProvider )] );

            var licenseConsumer = await licenseConsumingService.CreateConsumerAsync(
                LicenseConsumptionOptions.Default with { SubscriptionGracePeriod = _subscriptionGracePeriod },
                messages.Add );

            var canConsume = await licenseConsumer.TryConsumeAsync( LicenseRequirement.Any );

            Assert.Equal( mustSucceed, canConsume );

            if ( infringingComponent != null )
            {
#pragma warning disable CA1307 // Specify StringComparison for clarity
                Assert.Contains( infringingComponent.Name, messages.Single().Text );
#pragma warning restore CA1307 // Specify StringComparison for clarity
            }
        }

        [Fact]
        public async Task PassesWithValidSubscription()
        {
            var applicationInfo = CreateApplicationInfo( LicenseKeyProvider.DefaultSubscriptionExpirationDate );
            await this.AssertPassesAsync( applicationInfo );
        }

        [Fact]
        public async Task FailsWithInvalidSubscription()
        {
            var applicationInfo = CreateApplicationInfo( LicenseKeyProvider.DefaultSubscriptionExpirationDate.AddDays( 1 ) );
            await this.AssertFailsAsync( applicationInfo, applicationInfo );
        }

        [Fact]
        public async Task PassesWithValidSubscriptionForComponentRequiringSubscription()
        {
            var componentInfo = CreateComponentInfo( LicenseKeyProvider.DefaultSubscriptionExpirationDate, false );
            var applicationInfo = CreateApplicationInfo( LicenseKeyProvider.DefaultSubscriptionExpirationDate, componentInfo );
            await this.AssertPassesAsync( applicationInfo );
        }

        [Fact]
        public async Task FailsWithInvalidSubscriptionForComponentRequiringSubscription()
        {
            var componentInfo = CreateComponentInfo( LicenseKeyProvider.DefaultSubscriptionExpirationDate.AddDays( 1 ), false );
            var applicationInfo = CreateApplicationInfo( LicenseKeyProvider.DefaultSubscriptionExpirationDate, componentInfo );
            await this.AssertFailsAsync( applicationInfo, componentInfo );
        }

        [Fact]
        public async Task PassesWithInvalidSubscriptionForComponentNotRequiringSubscription()
        {
            var componentInfo = CreateComponentInfo( LicenseKeyProvider.DefaultSubscriptionExpirationDate.AddDays( 1 ), true );
            var applicationInfo = CreateApplicationInfo( LicenseKeyProvider.DefaultSubscriptionExpirationDate, componentInfo );
            await this.AssertPassesAsync( applicationInfo );
        }

        [Fact]
        public async Task FailsWithMultipleComponentsAndValidApplication()
        {
            var componentInfo1 = CreateComponentInfo( LicenseKeyProvider.DefaultSubscriptionExpirationDate.AddDays( 1 ), false );
            var componentInfo2 = CreateComponentInfo( LicenseKeyProvider.DefaultSubscriptionExpirationDate, false );
            var componentInfo3 = CreateComponentInfo( LicenseKeyProvider.DefaultSubscriptionExpirationDate.AddDays( 1 ), true );
            var applicationInfo = CreateApplicationInfo( LicenseKeyProvider.DefaultSubscriptionExpirationDate, componentInfo1, componentInfo2, componentInfo3 );
            await this.AssertFailsAsync( applicationInfo, componentInfo1 );
        }

        [Fact]
        public async Task FailsWithMultipleComponentsAndInvalidApplication()
        {
            var componentInfo1 = CreateComponentInfo( LicenseKeyProvider.DefaultSubscriptionExpirationDate, false );
            var componentInfo2 = CreateComponentInfo( LicenseKeyProvider.DefaultSubscriptionExpirationDate.AddDays( 1 ), true );
            var applicationInfo = CreateApplicationInfo( LicenseKeyProvider.DefaultSubscriptionExpirationDate.AddDays( 1 ), componentInfo1, componentInfo2 );
            await this.AssertFailsAsync( applicationInfo, applicationInfo );
        }
    }
}
