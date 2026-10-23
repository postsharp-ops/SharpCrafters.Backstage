// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using Metalama.Backstage;
using PostSharp.Backstage;
using SharpCrafters.Backstage.Application;
using SharpCrafters.Backstage.Configuration;
using SharpCrafters.Backstage.Configuration.Registry;
using SharpCrafters.Backstage.Diagnostics;
using SharpCrafters.Backstage.Extensibility;
using SharpCrafters.Backstage.Infrastructure;
using SharpCrafters.Backstage.Licensing;
using SharpCrafters.Backstage.Licensing.Consumption;
using SharpCrafters.Backstage.Licensing.Consumption.Requirements;
using SharpCrafters.Backstage.Licensing.Consumption.Sources;
using SharpCrafters.Backstage.Licensing.Registration;
using SharpCrafters.Backstage.ProcessClassification;
using SharpCrafters.Backstage.Telemetry;
using SharpCrafters.Backstage.Testing;
using SharpCrafters.Backstage.UserInterface;
using SharpCrafters.Backstage.UserInterface.Rss;
using SharpCrafters.Backstage.UserInterface.Toasts;
using SharpCrafters.Backstage.Welcome;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace SharpCrafters.Backstage.Tests.Extensibility;

/// <summary>
/// Tests the composite product that the Visual Studio extension uses to host Metalama and PostSharp in one process.
/// </summary>
/// <remarks>
/// Every test builds the service provider with <see cref="RegisterServiceExtensions.AddBackstageServices"/>, as the
/// extension does, and then replaces the services that touch the machine. The outer provider shares these substitutes
/// with the inner provider of PostSharp, so one <see cref="TestFileSystem"/> holds the configuration files of Metalama
/// and one <see cref="TestRegistryService"/> holds the registry keys of PostSharp.
/// </remarks>
public sealed class CompositeProductTests : IDisposable
{
    private readonly ITestOutputHelper _logger;
    private readonly TestLicenseKeyProvider _licenseKeys = new();
    private readonly TestRegistryService _registry = new();
    private readonly List<IDisposable> _serviceProviders = [];

    public CompositeProductTests( ITestOutputHelper logger )
    {
        this._logger = logger;
    }

    public void Dispose()
    {
        foreach ( var serviceProvider in this._serviceProviders )
        {
            serviceProvider.Dispose();
        }
    }

    private IServiceProvider CreateServiceProvider( bool addUserInterface = false, bool isLongRunningProcess = false )
    {
        var applicationInfo = new TestApplicationInfo( "Test", false, "2027.0.1", DateTime.Today )
        {
            IsLongRunningProcess = isLongRunningProcess, IsTelemetryEnabled = true
        };

        var product = BackstageProduct.CreateComposite( MetalamaProduct.Instance, PostSharpProduct.Instance );

        var options = new BackstageInitializationOptions( applicationInfo, product )
        {
            AddLicensing = true,
            AddSupportServices = true,
            AddUserInterface = addUserInterface,
            DetectToastNotifications = addUserInterface,
            OpenWelcomePage = addUserInterface,
            AddRssClient = addUserInterface,
            LicensingOptions = new LicensingInitializationOptions { UseTestAuthority = true, IgnoredLicenseSources = LicenseSourceKind.Unattended },
            AutoUploadTelemetry = false
        };

        var builder = new SimpleServiceProviderBuilder();
        builder.AddBackstageServices( options );

        // The substitutes are registered after the standard services, so that they replace them.
        var loggerFactory = new TestLoggerFactory( this._logger );
        builder.AddService( typeof(EarlyLoggerFactory), new EarlyLoggerFactory( loggerFactory ) );
        builder.AddService( typeof(ILoggerFactory), loggerFactory );
        builder.AddService( typeof(IDateTimeProvider), new TestDateTimeProvider() );
        builder.AddService( typeof(IFileSystem), serviceProvider => new TestFileSystem( serviceProvider ) );
        builder.AddService( typeof(IRegistryService), this._registry );
        builder.AddService( typeof(IHttpClientFactory), serviceProvider => new TestHttpClientFactory( serviceProvider ) );
        builder.AddService( typeof(IMachineIdProvider), new TestMachineIdProvider() );
        builder.AddService( typeof(IUserIdentityProvider), new TestUserIdentityProvider() );
        builder.AddService( typeof(IEnvironmentVariableProvider), new TestEnvironmentVariableProvider() );
        builder.AddService( typeof(IUserDeviceDetectionService), new TestUserDeviceDetectionService { IsInteractiveDevice = true } );
        builder.AddService( typeof(IUnattendedProcessDetector), new TestUnattendedProcessDetector() );

        if ( addUserInterface )
        {
            builder.AddService( typeof(IUserInterfaceService), serviceProvider => new TestUserInterfaceService( serviceProvider ) );
        }

        var serviceProvider = builder.ServiceProvider;
        this._serviceProviders.Add( (IDisposable) serviceProvider );

        return serviceProvider;
    }

    private static CompositeProductServices GetProducts( IServiceProvider serviceProvider )
        => serviceProvider.GetRequiredBackstageService<CompositeProductServices>();

    private static IEnumerable<string> GetRegisteredLicenseStrings( ProductServices product )
        => product.LicenseRegistrationService.RegisteredLicenses.Select( l => l.LicenseString );

    /// <summary>
    /// Tests that each product keeps its own configuration store. Metalama reads its licenses from its files and
    /// PostSharp from the registry, so a license that the extension wrote to the wrong store would be invisible to
    /// the build of the product that needs it.
    /// </summary>
    [Fact]
    public async Task EachProductKeepsItsOwnStore()
    {
        var serviceProvider = this.CreateServiceProvider();
        var products = GetProducts( serviceProvider );

        var metalamaConfiguration = products.Products[0].ServiceProvider.GetRequiredBackstageService<IConfigurationManager>();
        var postSharpConfiguration = products.Products[1].ServiceProvider.GetRequiredBackstageService<IConfigurationManager>();

        Assert.Same( serviceProvider.GetRequiredBackstageService<IConfigurationManager>(), metalamaConfiguration );
        Assert.NotSame( metalamaConfiguration, postSharpConfiguration );
        Assert.IsType<RegistryConfigurationManager>( postSharpConfiguration );

        var registryWritesBefore = this._registry.WriteCount;

        var registrationService = serviceProvider.GetRequiredBackstageService<ILicenseRegistrationService>();
        Assert.True( (await registrationService.RegisterLicenseAsync( this._licenseKeys.MetalamaProfessionalBusiness )).IsSuccess );

        // A Metalama key is written to the files of Metalama, not to the registry.
        Assert.Equal( registryWritesBefore, this._registry.WriteCount );

        Assert.True( (await registrationService.RegisterLicenseAsync( this._licenseKeys.PostSharpCaching )).IsSuccess );

        // A PostSharp key is written to the registry.
        Assert.True( this._registry.WriteCount > registryWritesBefore );
    }

    /// <summary>
    /// Tests that the composite service reports the licenses and the editions of every product. The license page of
    /// the extension lists what the user holds, and a license registered by PostSharp would otherwise be missing from it.
    /// </summary>
    [Fact]
    public async Task LicensesAndEditionsAreTheUnionOfTheProducts()
    {
        var serviceProvider = this.CreateServiceProvider();
        var products = GetProducts( serviceProvider );

        // Registered by each product directly, as the build of each product would.
        Assert.True( (await products.Products[0].LicenseRegistrationService.RegisterLicenseAsync( this._licenseKeys.MetalamaProfessionalBusiness )).IsSuccess );
        Assert.True( (await products.Products[1].LicenseRegistrationService.RegisterLicenseAsync( this._licenseKeys.PostSharpCaching )).IsSuccess );

        var registrationService = serviceProvider.GetRequiredBackstageService<ILicenseRegistrationService>();

        Assert.Equal(
            new[] { this._licenseKeys.MetalamaProfessionalBusiness, this._licenseKeys.PostSharpCaching }.OrderBy( s => s ),
            registrationService.RegisteredLicenses.Select( l => l.LicenseString ).OrderBy( s => s ) );

        var availableEditions = registrationService.AvailableEditions;
        Assert.Contains( availableEditions, e => MetalamaProduct.LicenseProductCatalog.SelfRegisteredEditions.Contains( e ) );
        Assert.Contains( availableEditions, e => PostSharpProduct.LicenseProductCatalog.SelfRegisteredEditions.Contains( e ) );

        // The union catalog lists the editions of both families, so an edition of each can be registered through it.
        var catalog = serviceProvider.GetRequiredBackstageService<ILicenseProductCatalog>();
        Assert.True( catalog.IsProductOfFamily( LicenseProduct.MetalamaProfessional ) );
        Assert.True( catalog.IsProductOfFamily( LicenseProduct.PostSharpCachingLibrary ) );
    }

    /// <summary>
    /// Tests where a license key is stored. A key goes to the product to which its product is native, so that the build
    /// of that product, which reads only its own store, finds it. A PostSharp Ultimate key is accepted by Metalama as
    /// well as by PostSharp, and it is stored by PostSharp, which is the only product of the two that it fully licenses.
    /// </summary>
    [Theory]
    [InlineData( nameof(TestLicenseKeyProvider.MetalamaProfessionalBusiness), 0 )]
    [InlineData( nameof(TestLicenseKeyProvider.PostSharpCaching), 1 )]
    [InlineData( nameof(TestLicenseKeyProvider.PostSharpUltimate), 1 )]
    [InlineData( nameof(TestLicenseKeyProvider.PostSharpFramework), 1 )]
    public async Task ALicenseKeyIsStoredByTheProductToWhichItIsNative( string licenseKeyName, int expectedProductIndex )
    {
        var licenseKey = (string) typeof(TestLicenseKeyProvider).GetProperty( licenseKeyName )!.GetValue( this._licenseKeys )!;

        var serviceProvider = this.CreateServiceProvider();
        var products = GetProducts( serviceProvider );
        var registrationService = serviceProvider.GetRequiredBackstageService<ILicenseRegistrationService>();

        Assert.True( (await registrationService.ValidateLicenseKeyAsync( licenseKey )).IsSuccess );
        Assert.Empty( registrationService.RegisteredLicenses );

        var result = await registrationService.RegisterLicenseAsync( licenseKey );
        Assert.True( result.IsSuccess, result.IsSuccess ? null : result.ErrorMessage );

        for ( var i = 0; i < products.Products.Length; i++ )
        {
            if ( i == expectedProductIndex )
            {
                Assert.Equal( [licenseKey], GetRegisteredLicenseStrings( products.Products[i] ) );
            }
            else
            {
                Assert.Empty( GetRegisteredLicenseStrings( products.Products[i] ) );
            }
        }

        // Removing the licenses removes them from every store.
        registrationService.RemoveLicenses();
        Assert.Empty( registrationService.RegisteredLicenses );
    }

    /// <summary>
    /// Tests that a license of either product unlocks the tooling of the extension. A user who bought PostSharp
    /// Ultimate registered it with PostSharp, in the registry, and the extension must not ask them for a Metalama key.
    /// </summary>
    [Fact]
    public async Task ALicenseOfAnyProductSatisfiesTheRequirement()
    {
        var serviceProvider = this.CreateServiceProvider();
        var products = GetProducts( serviceProvider );

        Assert.True( (await products.Products[1].LicenseRegistrationService.RegisterLicenseAsync( this._licenseKeys.PostSharpUltimate )).IsSuccess );

        var requirement = new ToolingLicenseRequirement( MetalamaProduct.Profile );

        // Metalama alone does not see the key, which is in the store of PostSharp.
        var metalamaConsumer = await products.Products[0].LicenseConsumptionService.CreateConsumerAsync();
        Assert.False( metalamaConsumer.TryConsume( requirement, showsToastNotification: false ) );

        var messages = new List<LicensingMessage>();
        var consumer = await serviceProvider.GetRequiredBackstageService<ILicenseConsumptionService>().CreateConsumerAsync();

        Assert.True( consumer.TryConsume( requirement, messages.Add, showsToastNotification: false ) );
        Assert.Empty( messages );
        Assert.Contains( consumer.Licenses, l => l.LicenseProduct == LicenseProduct.PostSharpUltimate );
    }

    /// <summary>
    /// Tests the message of a requirement that no product satisfies. The user is told once, and not once per product.
    /// </summary>
    [Fact]
    public async Task ARequirementThatNoProductSatisfiesIsReportedOnce()
    {
        var serviceProvider = this.CreateServiceProvider();

        var messages = new List<LicensingMessage>();
        var consumer = await serviceProvider.GetRequiredBackstageService<ILicenseConsumptionService>().CreateConsumerAsync();

        Assert.False( consumer.TryConsume( new ToolingLicenseRequirement( MetalamaProduct.Profile ), messages.Add, showsToastNotification: false ) );
        Assert.Single( messages );
        Assert.Equal( LicensingMessageKind.NoLicense, messages[0].Kind );
    }

    /// <summary>
    /// Tests that the one telemetry switch of the extension applies to both products. A user who turns telemetry off in
    /// Visual Studio expects PostSharp to stop reporting as well as Metalama.
    /// </summary>
    [Fact]
    public void TheTelemetryConsentIsWrittenToEveryProduct()
    {
        var serviceProvider = this.CreateServiceProvider().InitializeBackstageServices();
        var products = GetProducts( serviceProvider );

        var telemetryConfigurationService = serviceProvider.GetRequiredBackstageService<ITelemetryConfigurationService>();

        telemetryConfigurationService.SetConsent( TelemetryConsent.No );

        foreach ( var product in products.Products )
        {
            var configuration = product.ServiceProvider.GetRequiredBackstageService<IConfigurationManager>().Get<TelemetryConfiguration>();
            Assert.Equal( TelemetryConsent.No, configuration.UsageConsent );
            Assert.Equal( TelemetryConsent.No, configuration.ExceptionConsent );
        }

        telemetryConfigurationService.SetConsent( TelemetryScenario.Usage, TelemetryConsent.Yes );

        foreach ( var product in products.Products )
        {
            var configuration = product.ServiceProvider.GetRequiredBackstageService<IConfigurationManager>().Get<TelemetryConfiguration>();
            Assert.Equal( TelemetryConsent.Yes, configuration.UsageConsent );
            Assert.Equal( TelemetryConsent.No, configuration.ExceptionConsent );
        }
    }

    /// <summary>
    /// Tests that the composite product runs one pipeline of notifications, welcome page and RSS client, which is that
    /// of the primary product. Two pipelines would show every notification twice and open two welcome pages.
    /// </summary>
    [Fact]
    public void OnlyThePrimaryProductHasAUserInterface()
    {
        var serviceProvider = this.CreateServiceProvider( addUserInterface: true );
        var inner = GetProducts( serviceProvider ).Products[1].ServiceProvider;

        Assert.NotNull( serviceProvider.GetBackstageService<IToastNotificationService>() );
        Assert.NotNull( serviceProvider.GetBackstageService<IToastNotificationDetectionService>() );
        Assert.NotNull( serviceProvider.GetBackstageService<WelcomePageService>() );
        Assert.NotNull( serviceProvider.GetBackstageService<IRssClient>() );
        Assert.NotNull( serviceProvider.GetBackstageService<IMiniDumper>() );

        Assert.Null( inner.GetBackstageService<IToastNotificationService>() );
        Assert.Null( inner.GetBackstageService<IToastNotificationDetectionService>() );
        Assert.Null( inner.GetBackstageService<WelcomePageService>() );
        Assert.Null( inner.GetBackstageService<IRssClient>() );
        Assert.Null( inner.GetBackstageService<IUserInterfaceService>() );
        Assert.Null( inner.GetBackstageService<IMiniDumper>() );

        // The services that do not depend on the product are the instances of the outer provider.
        Assert.Same( serviceProvider.GetBackstageService<ILoggerFactory>(), inner.GetBackstageService<ILoggerFactory>() );
        Assert.Same( serviceProvider.GetBackstageService<IEventDispatcher>(), inner.GetBackstageService<IEventDispatcher>() );
        Assert.Same( serviceProvider.GetBackstageService<IFileSystem>(), inner.GetBackstageService<IFileSystem>() );
        Assert.Same( this._registry, inner.GetBackstageService<IRegistryService>() );

        // The services that depend on the product are not.
        Assert.Same( PostSharpProduct.Profile, inner.GetRequiredBackstageService<ProductProfile>() );
        Assert.Same( MetalamaProduct.Profile, serviceProvider.GetRequiredBackstageService<ProductProfile>() );
    }

    /// <summary>
    /// Tests that the inner service providers are created only when needed and disposed with the outer one. An inner
    /// provider that outlived the extension would keep watching the registry of PostSharp.
    /// </summary>
    [Fact]
    public void InnerServiceProvidersAreDisposedWithTheOuterOne()
    {
        var serviceProvider = this.CreateServiceProvider( isLongRunningProcess: true ).InitializeBackstageServices();
        var products = GetProducts( serviceProvider );

        Assert.False( products.Products[1].IsServiceProviderCreated );

        // Creating the configuration manager of PostSharp starts watching its registry keys.
        _ = products.Products[1].ServiceProvider.GetRequiredBackstageService<IConfigurationManager>();
        Assert.True( this._registry.WatcherCount > 0 );

        ((IDisposable) serviceProvider).Dispose();

        Assert.Equal( 0, this._registry.WatcherCount );
    }

    /// <summary>
    /// Tests that a composite product holds products of different families only. Two products of one family would
    /// share one store, and every change would be written to it twice.
    /// </summary>
    [Fact]
    public void AProductCannotBeAddedTwice()
    {
        Assert.Throws<ArgumentException>( () => BackstageProduct.CreateComposite( MetalamaProduct.Instance, MetalamaProduct.Instance ) );

        var composite = BackstageProduct.CreateComposite( MetalamaProduct.Instance, PostSharpProduct.Instance );
        Assert.Throws<ArgumentException>( () => BackstageProduct.CreateComposite( composite ) );
    }
}
