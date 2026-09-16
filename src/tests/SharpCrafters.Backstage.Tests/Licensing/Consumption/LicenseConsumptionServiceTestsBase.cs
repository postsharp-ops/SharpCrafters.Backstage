// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Licensing.Consumption;
using SharpCrafters.Backstage.Licensing.Consumption.Sources;
using SharpCrafters.Backstage.Licensing.Licenses;
using SharpCrafters.Backstage.Testing;
using SharpCrafters.Backstage.Tests.Licensing.Licenses;
using SharpCrafters.Backstage.Tests.Licensing.LicenseSources;
using System;
using Xunit;
using Xunit.Abstractions;

namespace SharpCrafters.Backstage.Tests.Licensing.Consumption;

public abstract class LicenseConsumptionServiceTestsBase : LicensingTestsBase
{
    private protected LicenseConsumptionServiceTestsBase(
        ITestOutputHelper logger,
        bool isTelemetryEnabled = false )
        : base( logger, isTelemetryEnabled: isTelemetryEnabled ) { }

    protected void SetBuildDate( DateTime buildDate )
    {
        ((TestApplicationInfo) this.ApplicationInfo).BuildDate = buildDate;
    }

    private protected InstrumentedLicenseWrapper CreateInstrumentedLicenseWrapper( string licenseString )
    {
        var licenseFactory = new LicenseFactory( this.ServiceProvider );
        Assert.True( licenseFactory.TryCreate( licenseString, out var license, out var errorMessage ) );
        Assert.Null( errorMessage );

        return new InstrumentedLicenseWrapper( license );
    }

    private protected ILicenseConsumptionService CreateConsumptionService( InstrumentedLicenseWrapper license )
    {
        // ReSharper disable once CoVariantArrayConversion
        var licenseSource = new TestLicenseSource( "test", license );

        var manager = this.CreateConsumptionService( licenseSource );

        return manager;
    }

    private protected ILicenseConsumptionService CreateConsumptionService( string licenseKey )
        => this.CreateConsumptionService( this.CreateInstrumentedLicenseWrapper( licenseKey ) );

    private protected ILicenseConsumptionService CreateConsumptionService( params ILicenseSource[] licenseSources )
    {
        return new LicenseConsumptionService( this.ServiceProvider, licenseSources );
    }

    private protected static void AssertCanConsume(
        ILicenseConsumptionService service,
        LicenseRequirement requirement,
        bool expectedCanConsume )
    {
        var consumer = service.CreateConsumer();
        var actualCanConsume = consumer.TryConsume( requirement );
        Assert.Equal( expectedCanConsume, actualCanConsume );
    }
}