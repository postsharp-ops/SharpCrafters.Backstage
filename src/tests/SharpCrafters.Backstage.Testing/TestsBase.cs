// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using JetBrains.Annotations;
using Metalama.Backstage;
using Metalama.Backstage.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SharpCrafters.Backstage.Application;
using SharpCrafters.Backstage.Configuration;
using SharpCrafters.Backstage.Diagnostics;
using SharpCrafters.Backstage.Extensibility;
using SharpCrafters.Backstage.Infrastructure;
using SharpCrafters.Backstage.Licensing;
using SharpCrafters.Backstage.Licensing.Audit;
using SharpCrafters.Backstage.Licensing.Consumption;
using SharpCrafters.Backstage.Licensing.Consumption.Sources;
using SharpCrafters.Backstage.Licensing.LicenseServer;
using SharpCrafters.Backstage.Licensing.Licenses;
using SharpCrafters.Backstage.Licensing.Registration;
using SharpCrafters.Backstage.Maintenance;
using SharpCrafters.Backstage.Repositories;
using SharpCrafters.Backstage.Serialization;
using SharpCrafters.Backstage.Telemetry;
using SharpCrafters.Backstage.Threading;
using SharpCrafters.Backstage.Tools;
using SharpCrafters.Backstage.UserInterface;
using SharpCrafters.Backstage.UserInterface.Rss;
using SharpCrafters.Backstage.UserInterface.Toasts;
using SharpCrafters.Backstage.VersionControl;
using SharpCrafters.Backstage.Welcome;
using System;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Xunit.Abstractions;
using ILoggerFactory = SharpCrafters.Backstage.Diagnostics.ILoggerFactory;

namespace SharpCrafters.Backstage.Testing
{
    [PublicAPI]
    public abstract class TestsBase
    {
        protected ITestOutputHelper Logger { get; }

        protected TestDateTimeProvider Time { get; } = new();

        protected TestFileSystem FileSystem => this._defaultTestContext.Value.FileSystem;

        protected TestEnvironmentVariableProvider EnvironmentVariableProvider { get; } = new();

        protected TestLoggerFactory Log { get; }

        /// <summary>
        /// Gets the substitute for the named locks, which uses no operating system object, records what the code
        /// under test does with its locks, and fails the test when the code under test breaks the locking
        /// discipline.
        /// </summary>
        /// <remarks>
        /// One instance stands for one machine, and it is shared by every service provider this test builds, so
        /// two components of the same test exclude each other exactly as two processes would.
        /// </remarks>
        protected TestNamedLockService Locks { get; }

        protected IServiceProvider ServiceProvider => this._defaultTestContext.Value.ServiceProvider;

        // May be null if the different implementation of IConfigurationManager is used.
        protected InMemoryConfigurationManager? ConfigurationManager => this._defaultTestContext.Value.ConfigurationManager;

        protected TestProcessExecutor ProcessExecutor { get; } = new();

        protected TestUserDeviceDetectionService UserDeviceDetection { get; } = new();

        /// <summary>
        /// Gets the substitute for the machine identifier, so that a test observes a pinned value instead of the
        /// identifier of the machine that runs the test.
        /// </summary>
        protected TestMachineIdProvider MachineIdProvider { get; } = new();

        /// <summary>
        /// Gets the substitute for the user and machine names, so that a test observes pinned values instead of those
        /// of the machine that runs the test.
        /// </summary>
        protected TestUserIdentityProvider UserIdentity { get; } = new();

        protected TestUserInterfaceService UserInterface => this._defaultTestContext.Value.UserInterface;

        protected BackstageBackgroundTasksService BackgroundTasks { get; } = new();

        /// <summary>
        /// Gets the observer that records the failures of the event dispatcher.
        /// </summary>
        protected TestEventDispatcherObserver EventObserver { get; } = new();

        /// <summary>
        /// Gets the event dispatcher of the test service provider.
        /// </summary>
        protected IEventDispatcher EventDispatcher => this.ServiceProvider.GetRequiredBackstageService<IEventDispatcher>();

        /// <summary>
        /// Waits until every published event has been delivered and every background task has completed. Events are
        /// delivered asynchronously, so a test that asserts on a reaction to an event must wait first.
        /// </summary>
        protected async Task DrainEventsAsync()
        {
            await this.EventDispatcher.CompleteAsync( CancellationToken.None );
            await this.BackgroundTasks.WhenNoPendingTaskAsync();

            // A handler of an event may have published another event.
            await this.EventDispatcher.CompleteAsync( CancellationToken.None );
        }

        protected TestHttpClientFactory HttpClientFactory => this._defaultTestContext.Value.HttpClientFactory;

        protected ILicenseRegistrationService LicenseRegistrationService => this._defaultTestContext.Value.LicenseRegistrationService;

        protected ITelemetryConfigurationService TelemetryConfigurationService => this._defaultTestContext.Value.TelemetryConfigurationService;

        protected TestRuntimeInformation RuntimeInformation => this._defaultTestContext.Value.RuntimeInformation;

        /// <summary>
        /// Gets the licensing authority provider registered in the service provider of the current test.
        /// </summary>
        protected ILicensingAuthorityProvider LicensingAuthorityProvider
            => this.ServiceProvider.GetRequiredBackstageService<ILicensingAuthorityProvider>();

        /// <summary>
        /// Creates the licensing authority provider registered in the service provider of the current test. The
        /// default implementation returns a provider of the test key, which signs and verifies a test license key.
        /// </summary>
        /// <param name="serviceProvider">The service provider under construction.</param>
        /// <returns>The licensing authority provider of the current test.</returns>
        protected virtual ILicensingAuthorityProvider CreateLicensingAuthorityProvider( IServiceProvider serviceProvider )
            => new TestLicensingAuthorityProvider( serviceProvider );

        /// <summary>
        /// Adds a group of license keys to the licensing configuration of the current test, beside the license keys
        /// that are already registered, exactly as a later version of Metalama writes it. See issue #1922.
        /// </summary>
        /// <param name="minimalVersion">The minimal version of Metalama that can consume the license keys of the group.</param>
        /// <param name="licenseKeys">The license keys of the group.</param>
        /// <remarks>
        /// A test that needs a group which the running version does not support has to write it, because the running
        /// version detects the minimal version of a license key from a format that it knows, and therefore never
        /// registers a license key that requires a version later than its own.
        /// </remarks>
        protected void AddLicenseGroup( string minimalVersion, params string[] licenseKeys )
        {
            var configurationManager = this.ConfigurationManager
                                       ?? throw new InvalidOperationException(
                                           "The current test does not use the in-memory configuration manager." );

            var configuration = configurationManager.Get<LicensingConfiguration>();

            var groups = configuration.LicensesByMinimalVersion ?? ImmutableDictionary<string, ImmutableArray<string?>>.Empty;

            configurationManager.Set(
                configuration with
                {
                    LicensesByMinimalVersion = groups.SetItem( minimalVersion, ImmutableArray.Create<string?>( licenseKeys ) )
                } );
        }

        private TestFileSystem? _uniqueFileSystem;
        private TestHttpClientFactory? _uniqueHttpClientFactory;

        protected IServiceCollection CloneServiceCollection()
        {
            var services = new ServiceCollection();

            foreach ( var service in this._defaultTestContext.Value.ServiceCollection )
            {
                services.Add( service );
            }

            return services;
        }

        protected TestsBase( ITestOutputHelper logger, IApplicationInfo? applicationInfo = null )
            : this( logger, new BackstageInitializationOptions( applicationInfo ?? new TestApplicationInfo(), MetalamaProduct.Instance ) { AutoUploadTelemetry = false } ) { }

        /// <summary>
        /// Method that can add services. 
        /// </summary>
        protected virtual void ConfigureServices( ServiceProviderBuilder services ) { }

        /// <summary>
        /// Method invoked just after the services are instantiated and initialized.
        /// </summary>
        protected virtual void OnAfterServicesCreated( Services services ) { }

        protected void EnsureServicesInitialized()
        {
            _ = this.ServiceProvider;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="TestsBase"/> class for a given product, which the test suite
        /// of a product passes so that its own catalog and profile are the ones under test.
        /// </summary>
        protected TestsBase( ITestOutputHelper logger, BackstageInitializationOptions? options )
        {
            this.Logger = logger;

            this.Log = new TestLoggerFactory( logger );
            this.Locks = new TestNamedLockService( logger.WriteLine );

            this._configureServicesAction = this.ConfigureServices;
            this._initializationOptions = options ?? new BackstageInitializationOptions( new TestApplicationInfo(), MetalamaProduct.Instance );

            this._defaultTestContext = new Lazy<Services>(
                () =>
                {
                    var services = this.CreateServices( this.ConfigureServicesAction, this.InitializationOptions );
                    this.OnAfterServicesCreated( services );

                    return services;
                } );
        }

        private readonly Lazy<Services> _defaultTestContext;
        private Action<ServiceProviderBuilder> _configureServicesAction;
        private BackstageInitializationOptions _initializationOptions;

        protected Action<ServiceProviderBuilder> ConfigureServicesAction
        {
            get => this._configureServicesAction;
            set
            {
                if ( this._defaultTestContext.IsValueCreated )
                {
                    throw new InvalidOperationException();
                }

                this._configureServicesAction = value;
            }
        }

        protected BackstageInitializationOptions InitializationOptions
        {
            get => this._initializationOptions;
            set
            {
                if ( this._defaultTestContext.IsValueCreated )
                {
                    throw new InvalidOperationException();
                }

                this._initializationOptions = value;
            }
        }

        protected IApplicationInfo ApplicationInfo
        {
            get => this._initializationOptions.ApplicationInfo;
            set
            {
                if ( this._defaultTestContext.IsValueCreated )
                {
                    throw new InvalidOperationException();
                }

                this._initializationOptions = this._initializationOptions with { ApplicationInfo = value };
            }
        }

        protected record Services(
            IServiceCollection ServiceCollection,
            IServiceProvider ServiceProvider,
            InMemoryConfigurationManager? ConfigurationManager,
            TestFileSystem FileSystem,
            TestUserInterfaceService UserInterface,
            TestHttpClientFactory HttpClientFactory,
            ILicenseRegistrationService LicenseRegistrationService,
            ITelemetryConfigurationService TelemetryConfigurationService,
            TestRuntimeInformation RuntimeInformation );

        private Services CreateServices( Action<ServiceProviderBuilder>? serviceBuilder = null, BackstageInitializationOptions? options = null )
        {
            var serviceCollection = this.CreateServiceCollection( serviceBuilder, options );

            var serviceProvider = serviceCollection.BuildServiceProvider().InitializeBackstageServices();

            return new Services(
                serviceCollection,
                serviceProvider,
                serviceProvider.GetRequiredBackstageService<IConfigurationManager>() as InMemoryConfigurationManager,
                (TestFileSystem) serviceProvider.GetRequiredBackstageService<IFileSystem>(),
                (TestUserInterfaceService) serviceProvider.GetRequiredBackstageService<IUserInterfaceService>(),
                (TestHttpClientFactory) serviceProvider.GetRequiredBackstageService<IHttpClientFactory>(),
                serviceProvider.GetRequiredBackstageService<ILicenseRegistrationService>(),
                serviceProvider.GetRequiredBackstageService<ITelemetryConfigurationService>(),
                (TestRuntimeInformation) serviceProvider.GetRequiredBackstageService<IRuntimeInformation>() );
        }

        private ServiceCollection CreateServiceCollection(
            Action<ServiceProviderBuilder>? serviceBuilder = null,
            BackstageInitializationOptions? options = null )
        {
            var serviceCollection = new ServiceCollection();
            options ??= new BackstageInitializationOptions( new TestApplicationInfo(), MetalamaProduct.Instance );

            serviceCollection
                .AddSingleton( new EarlyLoggerFactory( this.Log ) )
                .AddSingleton<IEventDispatcherObserver>( this.EventObserver )
                .AddSingleton<IEventDispatcher>( serviceProvider => new EventDispatcher( serviceProvider ) )
                .AddSingleton( serviceProvider => new UserInterfaceEventSubscriber( serviceProvider ) )
                .AddSingleton<ILoggerFactory>( this.Log )
                .AddSingleton<IApplicationInfoProvider>( new ApplicationInfoProvider( options.ApplicationInfo ) )
                .AddSingleton( options.Product.TelemetryOptions )
                .AddSingleton(
                    options.Product.UserInterfaceOptions with
                    {
                        OpenWelcomePage = options.OpenWelcomePage,
                        DetectToastNotifications = options.DetectToastNotifications,
                        AddRssClient = options.AddRssClient
                    } )
                .AddSingleton<IDateTimeProvider>( this.Time )
                .AddSingleton<IProcessExecutor>( this.ProcessExecutor )
                .AddSingleton<IVcsStatusService>( serviceProvider => new GitStatusService( serviceProvider ) )
                .AddSingleton<IRuntimeInformation>( _ => new TestRuntimeInformation() )
                .AddSingleton<IMachineIdProvider>( this.MachineIdProvider )
                .AddSingleton<IUserIdentityProvider>( this.UserIdentity )
                .AddSingleton<IPlatformInfo>( serviceProvider => new PlatformInfo( serviceProvider ) )
                .AddSingleton( this.BackgroundTasks )
                // As for the file system, there must be a single instance even when CloneServiceCollection is used:
                // one instance stands for one network, so a hook registered by the test is seen by every provider.
                .AddSingleton<IHttpClientFactory>( serviceProvider => this._uniqueHttpClientFactory ??= new TestHttpClientFactory( serviceProvider ) )
                .AddSingleton( options.Product.Profile )
                .AddSingleton<IWebLinks>( options.Product.WebLinks )
                .AddSingleton( _ => new RandomNumberGenerator( 0 ) )

                // We must always have a single instance of the file system even if we use CloneServiceCollection.
                .AddSingleton<IFileSystem>( serviceProvider => this._uniqueFileSystem ??= new TestFileSystem( serviceProvider ) )
                .AddSingleton<INamedLockService>( this.Locks )
                .AddSingleton<IEnvironmentVariableProvider>( this.EnvironmentVariableProvider )
                .AddSingleton<IRecoverableExceptionService>( new TestRecoverableExceptionService() )
                .AddSingleton<IUserDeviceDetectionService>( this.UserDeviceDetection )
                .AddSingleton<IJsonSerializationService>( _ => new JsonSerializationService( [BackstageJsonContext.Default, .. options.AdditionalJsonTypeInfoResolvers] ) )
                .AddSingleton<IConfigurationManager>( serviceProvider => new InMemoryConfigurationManager( serviceProvider ) )
                .AddSingleton<ITempFileManager>( serviceProvider => new TempFileManager( serviceProvider ) )
                .AddSingleton<ILicenseProductCatalog>( options.LicensingOptions.ProductCatalog ?? options.Product.LicenseProductCatalog )
                .AddSingleton( serviceProvider => new LicenseServerUrlValidator( serviceProvider ) )
                .AddSingleton( serviceProvider => new LicenseLeaseStore( serviceProvider ) )
                .AddSingleton( serviceProvider => new LicenseServerClient( serviceProvider, options.LicensingOptions ) )
                .AddSingleton<ILicenseRegistrationService>( serviceProvider => new LicenseRegistrationService( serviceProvider ) )
                .AddSingleton<ILicenseConsumptionService>(
                    serviceProvider => LicenseConsumptionServiceFactory.Create(
                        serviceProvider,
                        new LicensingInitializationOptions { IgnoredLicenseSources = LicenseSourceKind.Unattended } ) )
                .AddSingleton<IBackstageToolsExecutor>( serviceProvider => new BackstageToolsExecutor( serviceProvider ) )
                .AddSingleton<IBackstageToolsLocator>( serviceProvider => new BackstageToolsLocator( serviceProvider ) )
                .AddSingleton<IUserInterfaceService>( serviceProvider => new TestUserInterfaceService( serviceProvider ) )
                .AddSingleton<IToastNotificationService>( serviceProvider => new ToastNotificationService( serviceProvider ) )
                .AddSingleton<IToastNotificationStatusService>( serviceProvider => new ToastNotificationStatusService( serviceProvider ) )
                .AddSingleton<BackstageServicesInitializer>( serviceProvider => new BackstageServicesInitializer( serviceProvider, options ) )
                .AddSingleton<IIdeExtensionStatusService>( serviceProvider => new IdeExtensionStatusService( serviceProvider ) )
                .AddSingleton<IToastNotificationDetectionService>( serviceProvider => new ToastNotificationDetectionService( serviceProvider ) )
                .AddSingleton<IStandardDirectories>( serviceProvider => new StandardDirectories( serviceProvider ) )
                .AddSingleton<IBackstageToolsExtractor>(
                    serviceProvider => new BackstageToolsExtractor( serviceProvider, typeof(BackstageToolsExtensions).Assembly ) )
                .AddSingleton<ITelemetryConfigurationService>( serviceProvider => new TelemetryConfigurationService( serviceProvider ) )
                .AddSingleton<ITelemetryService>( serviceProvider => new TelemetryService( serviceProvider ) )
                .AddSingleton<IRepositoryConfigurationService>( serviceProvider => new RepositoryConfigurationService( serviceProvider ) )
                .AddSingleton<IUsageSessionFactory>( serviceProvider => new UsageSessionFactory( serviceProvider ) )
                .AddSingleton<IExceptionCapturer>( _ => new TestExceptionCapturer() )
                .AddSingleton( serviceProvider => new ExceptionSensitiveDataHelper( serviceProvider ) )
                .AddSingleton<TelemetryReportUploader>( serviceProvider => new TelemetryReportUploader( serviceProvider ) )
                .AddSingleton<ITelemetryUploader>( serviceProvider => new TelemetryUploader( serviceProvider ) )
                .AddSingleton<TelemetryLogger>( serviceProvider => new TelemetryLogger( serviceProvider ) )
                .AddSingleton<IRssClient>( serviceProvider => new RssClient( serviceProvider ) )
                .AddSingleton<ILicensingAuthorityProvider>( this.CreateLicensingAuthorityProvider )

                // The default that AddBackstageServices registers before the services of the product. This is not
                // that method, so the product's own registrations are not run here: a test that wants the answer of a
                // particular product registers it, and a test of the registration itself uses AddBackstageServices.
                .AddSingleton<ILicenseAuditKeyProvider>( _ => ReportContentLicenseAuditKeyProvider.Instance );

            if ( options.OpenWelcomePage )
            {
                serviceCollection.AddSingleton<WelcomePageService>( serviceProvider => new WelcomePageService( serviceProvider ) );
            }

            var serviceProviderBuilder =
                new ServiceProviderBuilder( ( type, instance ) => serviceCollection.AddSingleton( type, instance ) );

            // The test implementation may replace some services.
            serviceBuilder?.Invoke( serviceProviderBuilder );

            return serviceCollection;
        }
    }
}