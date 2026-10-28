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
using System.Runtime.InteropServices;
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

    // PostSharp detects the unattended mode with its own detector, which reads the machine unless this variable is set.
    private readonly TestEnvironmentVariableProvider _environment = new() { Environment = { ["POSTSHARP_FORCE_ATTENDED"] = "true" } };
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
        builder.AddService( typeof(IEnvironmentVariableProvider), this._environment );
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
    /// <remarks>
    /// Away from Windows, PostSharp keeps its configuration in files too, in a directory of its own, so the stores are
    /// still separate but no registry is written.
    /// </remarks>
    [Fact]
    public async Task EachProductKeepsItsOwnStore()
    {
        var serviceProvider = this.CreateServiceProvider();
        var products = GetProducts( serviceProvider );

        var metalamaConfiguration = products.Products[0].ServiceProvider.GetRequiredBackstageService<IConfigurationManager>();
        var postSharpConfiguration = products.Products[1].ServiceProvider.GetRequiredBackstageService<IConfigurationManager>();
        var isWindows = RuntimeInformation.IsOSPlatform( OSPlatform.Windows );

        Assert.Same( serviceProvider.GetRequiredBackstageService<IConfigurationManager>(), metalamaConfiguration );
        Assert.NotSame( metalamaConfiguration, postSharpConfiguration );

        if ( isWindows )
        {
            Assert.IsType<RegistryConfigurationManager>( postSharpConfiguration );
        }

        var registryWritesBefore = this._registry.WriteCount;

        var registrationService = serviceProvider.GetRequiredBackstageService<ILicenseRegistrationService>();
        Assert.True( (await registrationService.RegisterLicenseAsync( this._licenseKeys.MetalamaProfessionalBusiness )).IsSuccess );

        // A Metalama key is written to the files of Metalama, not to the registry.
        Assert.Equal( registryWritesBefore, this._registry.WriteCount );

        Assert.True( (await registrationService.RegisterLicenseAsync( this._licenseKeys.PostSharpCaching )).IsSuccess );

        // A PostSharp key is written to the store of PostSharp, which is the registry on Windows.
        Assert.Equal( isWindows, this._registry.WriteCount > registryWritesBefore );
        Assert.Equal( [this._licenseKeys.MetalamaProfessionalBusiness], GetRegisteredLicenseStrings( products.Products[0] ) );
        Assert.Equal( [this._licenseKeys.PostSharpCaching], GetRegisteredLicenseStrings( products.Products[1] ) );
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
    /// Tests the message of a requirement that no product satisfies while a product holds a license. The user holds a
    /// license that is not enough, and being told that they hold none would send them looking for a key they have
    /// already registered.
    /// </summary>
    [Fact]
    public async Task AFailureNamesTheLicensesOfEveryProduct()
    {
        var serviceProvider = this.CreateServiceProvider();
        var products = GetProducts( serviceProvider );

        // PostSharp Caching does not license the tooling, and Metalama holds nothing.
        Assert.True( (await products.Products[1].LicenseRegistrationService.RegisterLicenseAsync( this._licenseKeys.PostSharpCaching )).IsSuccess );

        var messages = new List<LicensingMessage>();
        var consumer = await serviceProvider.GetRequiredBackstageService<ILicenseConsumptionService>().CreateConsumerAsync();

        Assert.False( consumer.TryConsume( new ToolingLicenseRequirement( MetalamaProduct.Profile ), messages.Add, showsToastNotification: false ) );
        var message = Assert.Single( messages );
        Assert.Equal( LicensingMessageKind.RequirementNotSatisfied, message.Kind );
        Assert.Contains( "PostSharp Caching", message.Text, StringComparison.Ordinal );
    }

    /// <summary>
    /// Tests that a license key of the project is given only to the product that owns it. Another product would report
    /// that the key belongs to another product family, and the build would show a warning about a key that works.
    /// </summary>
    [Fact]
    public async Task AProjectLicenseKeyIsReportedOnlyByItsProduct()
    {
        var serviceProvider = this.CreateServiceProvider();

        var messages = new List<LicensingMessage>();

        var consumer = await serviceProvider.GetRequiredBackstageService<ILicenseConsumptionService>()
            .CreateConsumerAsync( new LicenseConsumptionOptions { ProjectLicenseKey = this._licenseKeys.MetalamaProfessionalBusiness }, messages.Add );

        Assert.True( consumer.TryConsume( new ToolingLicenseRequirement( MetalamaProduct.Profile ), messages.Add, showsToastNotification: false ) );
        Assert.Empty( messages );
    }

    /// <summary>
    /// Tests that a pasted license key goes to the same product as the key itself. A key copied from an e-mail often
    /// carries line breaks or quotes, and a PostSharp key read as garbage would go to Metalama, which would then store
    /// it where PostSharp never reads.
    /// </summary>
    [Fact]
    public async Task APastedLicenseKeyIsStoredByItsProduct()
    {
        var serviceProvider = this.CreateServiceProvider();
        var products = GetProducts( serviceProvider );
        var licenseKey = this._licenseKeys.PostSharpUltimate;

        var result = await serviceProvider.GetRequiredBackstageService<ILicenseRegistrationService>()
            .RegisterLicenseAsync( "\"" + licenseKey.Insert( 10, Environment.NewLine ) + "\" " );

        Assert.True( result.IsSuccess, result.IsSuccess ? null : result.ErrorMessage );
        Assert.Empty( GetRegisteredLicenseStrings( products.Products[0] ) );
        Assert.Single( GetRegisteredLicenseStrings( products.Products[1] ) );
    }

    /// <summary>
    /// Tests that a usage session, which turns an unset consent into a consent without asking the user, does not
    /// change the refusal of another product. A user who refused telemetry in PostSharp must not find it enabled because
    /// they opened Visual Studio.
    /// </summary>
    [Fact]
    public void AnAutomaticConsentDoesNotOverrideTheRefusalOfAnotherProduct()
    {
        var serviceProvider = this.CreateServiceProvider().InitializeBackstageServices();
        var products = GetProducts( serviceProvider );

        products.Products[1].TelemetryConfigurationService.SetConsent( TelemetryScenario.Usage, TelemetryConsent.No );

        var telemetryConfigurationService = serviceProvider.GetRequiredBackstageService<ITelemetryConfigurationService>();
        Assert.True( telemetryConfigurationService.CompareExchangeConsent( TelemetryScenario.Usage, TelemetryConsent.Yes, TelemetryConsent.Default ) );

        Assert.Equal( TelemetryConsent.Yes, GetUsageConsent( products.Products[0] ) );
        Assert.Equal( TelemetryConsent.No, GetUsageConsent( products.Products[1] ) );

        static TelemetryConsent GetUsageConsent( ProductServices product )
            => product.ServiceProvider.GetRequiredBackstageService<IConfigurationManager>().Get<TelemetryConfiguration>().UsageConsent;
    }

    /// <summary>
    /// Tests that the opt-out environment variable of the primary product wins over the consent of another product.
    /// The outer pipeline is that of the primary product, and a user who set the variable expects it to report nothing.
    /// </summary>
    [Fact]
    public void TheProcessLevelOptOutOfThePrimaryProductWins()
    {
        this._environment.Environment["METALAMA_TELEMETRY_OPT_OUT"] = "true";

        var serviceProvider = this.CreateServiceProvider().InitializeBackstageServices();
        var products = GetProducts( serviceProvider );

        products.Products[1].TelemetryConfigurationService.SetConsent( TelemetryScenario.Exception, TelemetryConsent.Yes );
        Assert.Equal( TelemetryConsent.Yes, products.Products[1].TelemetryConfigurationService.GetEffectiveConsent( TelemetryScenario.Exception ) );

        var (consent, reason) = serviceProvider.GetRequiredBackstageService<ITelemetryConfigurationService>()
            .GetEffectiveConsentAndReason( TelemetryScenario.Exception );

        Assert.Equal( TelemetryConsent.No, consent );
        Assert.Equal( TelemetryDisabledReason.EnvironmentVariableOptOut, reason );
    }

    /// <summary>
    /// Tests that an inner service provider is usable as soon as it exists. Its license audit reads the telemetry
    /// configuration directly, without going through the composite service, and an uninitialized configuration would
    /// throw instead of queuing the audit.
    /// </summary>
    [Fact]
    public void TheServicesOfAnInnerProviderAreInitialized()
    {
        var serviceProvider = this.CreateServiceProvider().InitializeBackstageServices();
        var inner = GetProducts( serviceProvider ).Products[1].ServiceProvider;

        _ = inner.GetRequiredBackstageService<ITelemetryConfigurationService>().GetEffectiveConsent( TelemetryScenario.Usage );
        _ = inner.GetRequiredBackstageService<IUnattendedProcessDetector>().IsCurrentProcessUnattended;
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
    /// Tests that the effective consent is the maximal one over all products. A user who allowed PostSharp to report
    /// has allowed the extension that hosts it to report, even when Metalama is not allowed to.
    /// </summary>
    [Fact]
    public void TheEffectiveConsentIsTheMaximalOneOverAllProducts()
    {
        var serviceProvider = this.CreateServiceProvider().InitializeBackstageServices();
        var products = GetProducts( serviceProvider );
        var telemetryConfigurationService = serviceProvider.GetRequiredBackstageService<ITelemetryConfigurationService>();

        products.Products[0].TelemetryConfigurationService.SetConsent( TelemetryScenario.Exception, TelemetryConsent.No );
        products.Products[1].TelemetryConfigurationService.SetConsent( TelemetryScenario.Exception, TelemetryConsent.Yes );

        Assert.Equal( TelemetryConsent.No, products.Products[0].TelemetryConfigurationService.GetEffectiveConsent( TelemetryScenario.Exception ) );
        Assert.Equal( TelemetryConsent.Yes, products.Products[1].TelemetryConfigurationService.GetEffectiveConsent( TelemetryScenario.Exception ) );
        Assert.Equal( TelemetryConsent.Yes, telemetryConfigurationService.GetEffectiveConsent( TelemetryScenario.Exception ) );

        // Default, which asks the user, is more permissive than No.
        products.Products[1].TelemetryConfigurationService.SetConsent( TelemetryScenario.Exception, TelemetryConsent.Default );
        Assert.Equal( TelemetryConsent.Default, telemetryConfigurationService.GetEffectiveConsent( TelemetryScenario.Exception ) );

        products.Products[1].TelemetryConfigurationService.SetConsent( TelemetryScenario.Exception, TelemetryConsent.No );
        Assert.Equal( TelemetryConsent.No, telemetryConfigurationService.GetEffectiveConsent( TelemetryScenario.Exception ) );
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
        if ( RuntimeInformation.IsOSPlatform( OSPlatform.Windows ) )
        {
            Assert.Same( this._registry, inner.GetBackstageService<IRegistryService>() );
        }

        // The telemetry of the inner provider stays in its queue: it uploads nothing and sends no analytics.
        Assert.NotSame( serviceProvider.GetBackstageService<ITelemetryUploader>(), inner.GetBackstageService<ITelemetryUploader>() );
        Assert.False( inner.GetRequiredBackstageService<ITelemetryUploader>().StartUpload( force: true ) );
        Assert.Null( inner.GetBackstageService<MatomoUploader>() );

        // The unattended mode can be forced for each product separately, so each product has its own detector.
        Assert.NotSame( serviceProvider.GetBackstageService<IUnattendedProcessDetector>(), inner.GetBackstageService<IUnattendedProcessDetector>() );

        // The services that depend on the product are not.
        Assert.Same( PostSharpProduct.Profile, inner.GetRequiredBackstageService<ProductProfile>() );
        Assert.Same( MetalamaProduct.Profile, serviceProvider.GetRequiredBackstageService<ProductProfile>() );
    }

    /// <summary>
    /// Tests that the inner service providers are created only when needed and disposed with the outer one. An inner
    /// provider that outlived the extension would keep watching the registry of PostSharp.
    /// </summary>
    [PlatformFact( TestPlatforms.Windows )]
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
