// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using JetBrains.Annotations;
using Metalama.Backstage;
using Microsoft.Extensions.DependencyInjection;
using SharpCrafters.Backstage.Application;
using SharpCrafters.Backstage.Configuration;
using SharpCrafters.Backstage.Diagnostics;
using SharpCrafters.Backstage.Extensibility;
using SharpCrafters.Backstage.Infrastructure;
using SharpCrafters.Backstage.Maintenance;
using SharpCrafters.Backstage.ProcessClassification;
using SharpCrafters.Backstage.Repositories;
using SharpCrafters.Backstage.Serialization;
using SharpCrafters.Backstage.Telemetry;
using SharpCrafters.Backstage.Threading;
using System;
using System.Collections.Generic;
using System.IO;

namespace SharpCrafters.Backstage.Testing;

/// <summary>
/// A self-contained set of Backstage services that runs the real <see cref="ITelemetryService"/> on an in-memory file system,
/// and records the exception reports and the usage sessions instead of sending them.
/// </summary>
/// <remarks>
/// <para>
/// A test of a product that hosts Backstage uses this class to verify the telemetry that its own code produces. It registers
/// <see cref="TelemetryService"/> in the services of the product, creates a repository with <see cref="CreateRepository"/>, and
/// asserts on <see cref="ExceptionCapturer"/>, <see cref="UsageSessionFactory"/> and <see cref="GetLocalCrashReports"/>. The
/// policy is the policy of the real service, so the consent of the user and the <c>metalama.json</c> file of the repository are
/// evaluated as in production.
/// </para>
/// <para>
/// An exception has two outputs. The local crash report is written by the real <c>LocalExceptionReporter</c> regardless of the
/// policy, and <see cref="GetLocalCrashReports"/> lists it. The report for upload is created only when the policy allows
/// telemetry, and <see cref="ExceptionCapturer"/> records it instead.
/// </para>
/// <para>
/// The capturer and the session factory are the internal seams that a telemetry context calls once its policy allows the
/// scenario. A product cannot replace them itself, because they are internal to Backstage.
/// </para>
/// <para>
/// The telemetry configuration reads the process-level gates, which are the opt-out environment variable and the detection of
/// unattended processes, when it is initialized. It is initialized when <see cref="TelemetryService"/> is first accessed. A
/// test that sets <see cref="EnvironmentVariables"/> or <see cref="UnattendedProcessDetector"/> must therefore do it before it
/// accesses <see cref="TelemetryService"/>. A change made afterwards has no effect.
/// </para>
/// <para>
/// When the product registers its own <see cref="IRepositoryConfigurationReader"/>, as PostSharp does for its XML
/// <c>postsharp.config</c> file, the environment registers it too, so that a repository configuration file is read as in
/// production. <see cref="CreateRepository"/> then writes no configuration file, because the format of the file is the
/// format of the product. The test writes the file itself through <see cref="FileSystem"/>.
/// </para>
/// </remarks>
[PublicAPI]
public sealed class TestTelemetryEnvironment
{
    private readonly Lazy<ITelemetryService> _telemetryService;
    private readonly bool _hasProductRepositoryConfigurationReader;

    /// <summary>
    /// Initializes a new instance of the <see cref="TestTelemetryEnvironment"/> class.
    /// </summary>
    /// <param name="product">The product whose profile and telemetry options are used. The default is <see cref="MetalamaProduct.Instance"/>.</param>
    /// <param name="applicationInfo">The application. The default is a <see cref="TestApplicationInfo"/> for which telemetry is enabled.</param>
    public TestTelemetryEnvironment( BackstageProduct? product = null, IApplicationInfo? applicationInfo = null )
    {
        product ??= MetalamaProduct.Instance;
        applicationInfo ??= new TestApplicationInfo { IsTelemetryEnabled = true };

        // The product may read its repository configuration file with its own reader. Only this registration of the product is
        // taken, because the other services of the product are not needed by the telemetry services and may require services
        // that this environment does not provide. The registration does not create any service.
        Func<IServiceProvider, object>? repositoryConfigurationReaderFactory = null;

        product.RegisterServices(
            new ServiceProviderBuilder(
                ( type, factory ) =>
                {
                    if ( type == typeof(IRepositoryConfigurationReader) )
                    {
                        repositoryConfigurationReaderFactory = factory;
                    }
                } ) );

        this._hasProductRepositoryConfigurationReader = repositoryConfigurationReaderFactory != null;

        var services = new ServiceCollection()
            .AddSingleton( new EarlyLoggerFactory() )
            .AddSingleton<IDateTimeProvider>( this.Time )
            .AddSingleton<IEnvironmentVariableProvider>( this.EnvironmentVariables )
            .AddSingleton<IUnattendedProcessDetector>( this.UnattendedProcessDetector )
            .AddSingleton<IRuntimeInformation>( new TestRuntimeInformation() )
            .AddSingleton<IApplicationInfoProvider>( new ApplicationInfoProvider( applicationInfo ) )
            .AddSingleton( product.Profile )
            .AddSingleton( product.TelemetryOptions )
            .AddSingleton( new RandomNumberGenerator( 0 ) )
            .AddSingleton( new BackstageBackgroundTasksService() )
            .AddSingleton<INamedLockService>( new TestNamedLockService() )
            .AddSingleton<IEventDispatcher>( serviceProvider => new EventDispatcher( serviceProvider ) )
            .AddSingleton<IFileSystem>( serviceProvider => new TestFileSystem( serviceProvider ) )
            .AddSingleton<IJsonSerializationService>( _ => new JsonSerializationService( [BackstageJsonContext.Default] ) )
            .AddSingleton<IConfigurationManager>( serviceProvider => new InMemoryConfigurationManager( serviceProvider ) )
            .AddSingleton<IStandardDirectories>( serviceProvider => new StandardDirectories( serviceProvider ) )
            .AddSingleton<ITempFileManager>( serviceProvider => new TempFileManager( serviceProvider ) )
            .AddSingleton( serviceProvider => new ExceptionSensitiveDataHelper( serviceProvider ) )
            .AddSingleton( serviceProvider => new LocalExceptionReporter( serviceProvider ) )
            .AddSingleton<ITelemetryConfigurationService>( serviceProvider => new TelemetryConfigurationService( serviceProvider ) )
            .AddSingleton<IRepositoryConfigurationService>( serviceProvider => new RepositoryConfigurationService( serviceProvider ) )
            .AddSingleton<IUsageSessionStore>( serviceProvider => new FileUsageSessionStore( serviceProvider ) )
            .AddSingleton<IUsageSessionFactory>( this.UsageSessionFactory )
            .AddSingleton<IExceptionCapturer>(
                serviceProvider => new TestExceptionCapturer( serviceProvider.GetRequiredBackstageService<LocalExceptionReporter>() ) )
            .AddSingleton<ITelemetryService>( serviceProvider => new TelemetryService( serviceProvider ) );

        if ( repositoryConfigurationReaderFactory != null )
        {
            services.AddSingleton( typeof(IRepositoryConfigurationReader), repositoryConfigurationReaderFactory );
        }

        this.ServiceProvider = services.BuildServiceProvider();
        this.FileSystem = (TestFileSystem) this.ServiceProvider.GetRequiredBackstageService<IFileSystem>();
        this.ExceptionCapturer = (TestExceptionCapturer) this.ServiceProvider.GetRequiredBackstageService<IExceptionCapturer>();

        this._telemetryService = new Lazy<ITelemetryService>(
            () =>
            {
                // In production, BackstageServicesInitializer initializes the telemetry configuration when Backstage starts.
                // Here, it is initialized on first use, so that a test can configure the process-level gates before.
                ((TelemetryConfigurationService) this.ServiceProvider.GetRequiredBackstageService<ITelemetryConfigurationService>()).Initialize();

                return this.ServiceProvider.GetRequiredBackstageService<ITelemetryService>();
            } );
    }

    /// <summary>
    /// Gets the service provider that contains the telemetry services.
    /// </summary>
    public IServiceProvider ServiceProvider { get; }

    /// <summary>
    /// Gets the real telemetry service, which the test registers in the services of the product under test.
    /// </summary>
    /// <remarks>
    /// The first access initializes the telemetry configuration, which reads <see cref="EnvironmentVariables"/> and
    /// <see cref="UnattendedProcessDetector"/>.
    /// </remarks>
    public ITelemetryService TelemetryService => this._telemetryService.Value;

    /// <summary>
    /// Gets the in-memory file system on which the repositories and their repository configuration files are created.
    /// </summary>
    public TestFileSystem FileSystem { get; }

    /// <summary>
    /// Gets the clock of the environment.
    /// </summary>
    public TestDateTimeProvider Time { get; } = new();

    /// <summary>
    /// Gets the environment variables of the environment, which are empty by default. Set them before the first access to
    /// <see cref="TelemetryService"/>.
    /// </summary>
    public TestEnvironmentVariableProvider EnvironmentVariables { get; } = new();

    /// <summary>
    /// Gets the detector of unattended processes. By default, the process is not unattended. Configure it before the first
    /// access to <see cref="TelemetryService"/>.
    /// </summary>
    public TestUnattendedProcessDetector UnattendedProcessDetector { get; } = new();

    /// <summary>
    /// Gets the capturer that records the exception reports that a telemetry context allowed.
    /// </summary>
    public TestExceptionCapturer ExceptionCapturer { get; }

    /// <summary>
    /// Gets the factory that records the usage sessions that a telemetry context allowed.
    /// </summary>
    public RecordingUsageSessionFactory UsageSessionFactory { get; } = new();

    /// <summary>
    /// Gets the paths of the local crash reports that were written on the in-memory file system.
    /// </summary>
    /// <remarks>
    /// Only the files named as the local exception reporter names them are listed, because the crash report directory also
    /// contains the files of the temporary file manager.
    /// </remarks>
    public IReadOnlyList<string> GetLocalCrashReports()
    {
        var directory = this.ServiceProvider.GetRequiredBackstageService<IStandardDirectories>().CrashReportsDirectory;

        return this.FileSystem.DirectoryExists( directory ) ? this.FileSystem.GetFiles( directory, "exception-*.txt" ) : [];
    }

    /// <summary>
    /// Creates a git repository on the in-memory file system, optionally with a JSON repository configuration file, such as
    /// <c>metalama.json</c>, that enables or disables telemetry.
    /// </summary>
    /// <param name="rootDirectory">The root directory of the repository.</param>
    /// <param name="telemetryEnabled">The value of the <c>telemetry.enabled</c> setting, or <see langword="null"/> to create no repository configuration file.</param>
    /// <exception cref="InvalidOperationException"><paramref name="telemetryEnabled"/> is not <see langword="null"/>, and the product has no repository configuration file.</exception>
    /// <exception cref="NotSupportedException"><paramref name="telemetryEnabled"/> is not <see langword="null"/>, and the product reads its repository configuration file with its own reader, so the file is not JSON.</exception>
    public void CreateRepository( string rootDirectory, bool? telemetryEnabled )
    {
        if ( telemetryEnabled != null && this._hasProductRepositoryConfigurationReader )
        {
            throw new NotSupportedException(
                "The product reads its repository configuration file with its own reader. Create the repository without a configuration file, and write the file in the format of the product through the FileSystem property." );
        }

        this.FileSystem.CreateDirectory( Path.Combine( rootDirectory, ".git" ) );

        if ( telemetryEnabled != null )
        {
            var fileName = this.ServiceProvider.GetRequiredBackstageService<ProductProfile>().RepositoryConfigurationFileName
                           ?? throw new InvalidOperationException( "The product has no repository configuration file." );

            this.FileSystem.WriteAllText(
                Path.Combine( rootDirectory, fileName ),
                $$"""{ "telemetry": { "enabled": {{(telemetryEnabled.Value ? "true" : "false")}} } }""" );
        }
    }
}
