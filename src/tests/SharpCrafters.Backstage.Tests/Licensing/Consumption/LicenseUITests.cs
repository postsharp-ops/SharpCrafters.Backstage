// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Licensing.Consumption;
using SharpCrafters.Backstage.UserInterface.Toasts;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace SharpCrafters.Backstage.Tests.Licensing.Consumption;

// ReSharper disable once InconsistentNaming
public sealed class LicenseUITests : LicenseConsumptionServiceTestsBase
{
    public LicenseUITests( ITestOutputHelper logger ) : base( logger ) { }

    [Fact]
    public async Task NotificationShownWhenMissingRequirement()
    {
        var consumer = await this.CreateConsumptionService().CreateConsumerAsync();
        Assert.False( await consumer.TryConsumeAsync( new DelegateLicenseRequirement( _ => false ) ) );
        await this.DrainEventsAsync();
        Assert.NotEmpty( this.UserInterface.Notifications );
        Assert.Equal( ToastNotificationKinds.RequiresLicense, this.UserInterface.Notifications.Single().Kind );
    }

    [Fact]
    public async Task NotificationNotShownWhenFulfilledRequirement()
    {
        var consumer = await this.CreateConsumptionService( LicenseKeyProvider.MetalamaProfessionalBusiness ).CreateConsumerAsync();
        Assert.True( await consumer.TryConsumeAsync( LicenseRequirement.Any ) );
        await this.DrainEventsAsync();
        Assert.Empty( this.UserInterface.Notifications );
    }
}