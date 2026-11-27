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
using SharpCrafters.Backstage.ProcessClassification;
using SharpCrafters.Backstage.Repositories;
using SharpCrafters.Backstage.Serialization;
using SharpCrafters.Backstage.Telemetry;
using SharpCrafters.Backstage.Threading;
using System;
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
/// asserts on <see cref="ExceptionCapturer"/> and <see cref="UsageSessionFactory"/>. The policy is the policy of the real
/// service, so the consent of the user and the <c>metalama.json</c> file of the repository are evaluated as in production.
/// </para>
/// <para>
/// The capturer and the session factory are the internal seams that a telemetry context calls once its policy allows the
/// scenario. A product cannot replace them itself, because they are internal to Backstage.
/// </para>
/// </remarks>
[PublicAPI]
public sealed class TestTelemetryEnvironment
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TestTelemetryEnvironment"/> class.
    /// </summary>
    /// <param name="product">The product whose profile and telemetry options are used. The default is <see cref="MetalamaProduct.Instance"/>.</param>
    /// <param name="applicationInfo">The application. The default is a <see cref="TestApplicationInfo"/> for which telemetry is enabled.</param>
    public TestTelemetryEnvironment( BackstageProduct? product = null, IApplicationInfo? applicationInfo = null )
    {
        product ??= MetalamaProduct.Instance;
        applicationInfo ??= new TestApplicationInfo { IsTelemetryEnabled = true };

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
            .AddSingleton<INamedLockService>( new TestNamedLockService() )
            .AddSingleton<IEventDispatcher>( serviceProvider => new EventDispatcher( serviceProvider ) )
            .AddSingleton<IFileSystem>( serviceProvider => new TestFileSystem( serviceProvider ) )
            .AddSingleton<IJsonSerializationService>( _ => new JsonSerializationService( [BackstageJsonContext.Default] ) )
            .AddSingleton<IConfigurationManager>( serviceProvider => new InMemoryConfigurationManager( serviceProvider ) )
            .AddSingleton<IStandardDirectories>( serviceProvider => new StandardDirectories( serviceProvider ) )
            .AddSingleton<ITelemetryConfigurationService>( serviceProvider => new TelemetryConfigurationService( serviceProvider ) )
            .AddSingleton<IRepositoryConfigurationService>( serviceProvider => new RepositoryConfigurationService( serviceProvider ) )
            .AddSingleton<IUsageSessionStore>( serviceProvider => new FileUsageSessionStore( serviceProvider ) )
            .AddSingleton<IUsageSessionFactory>( this.UsageSessionFactory )
            .AddSingleton<IExceptionCapturer>( this.ExceptionCapturer )
            .AddSingleton<ITelemetryService>( serviceProvider => new TelemetryService( serviceProvider ) );

        this.ServiceProvider = services.BuildServiceProvider();

        // In production, BackstageServicesInitializer initializes the telemetry configuration when Backstage starts.
        ((TelemetryConfigurationService) this.ServiceProvider.GetRequiredBackstageService<ITelemetryConfigurationService>()).Initialize();

        this.FileSystem = (TestFileSystem) this.ServiceProvider.GetRequiredBackstageService<IFileSystem>();
        this.TelemetryService = this.ServiceProvider.GetRequiredBackstageService<ITelemetryService>();
    }

    /// <summary>
    /// Gets the service provider that contains the telemetry services.
    /// </summary>
    public IServiceProvider ServiceProvider { get; }

    /// <summary>
    /// Gets the real telemetry service, which the test registers in the services of the product under test.
    /// </summary>
    public ITelemetryService TelemetryService { get; }

    /// <summary>
    /// Gets the in-memory file system on which the repositories and their <c>metalama.json</c> files are created.
    /// </summary>
    public TestFileSystem FileSystem { get; }

    /// <summary>
    /// Gets the clock of the environment.
    /// </summary>
    public TestDateTimeProvider Time { get; } = new();

    /// <summary>
    /// Gets the environment variables of the environment, which are empty by default.
    /// </summary>
    public TestEnvironmentVariableProvider EnvironmentVariables { get; } = new();

    /// <summary>
    /// Gets the detector of unattended processes. By default, the process is not unattended.
    /// </summary>
    public TestUnattendedProcessDetector UnattendedProcessDetector { get; } = new();

    /// <summary>
    /// Gets the capturer that records the exception reports that a telemetry context allowed.
    /// </summary>
    public TestExceptionCapturer ExceptionCapturer { get; } = new();

    /// <summary>
    /// Gets the factory that records the usage sessions that a telemetry context allowed.
    /// </summary>
    public RecordingUsageSessionFactory UsageSessionFactory { get; } = new();

    /// <summary>
    /// Creates a git repository on the in-memory file system, optionally with a repository configuration file that enables or
    /// disables telemetry.
    /// </summary>
    /// <param name="rootDirectory">The root directory of the repository.</param>
    /// <param name="telemetryEnabled">The value of the <c>telemetry.enabled</c> setting, or <see langword="null"/> to create no repository configuration file.</param>
    /// <exception cref="InvalidOperationException"><paramref name="telemetryEnabled"/> is not <see langword="null"/>, and the product has no repository configuration file.</exception>
    public void CreateRepository( string rootDirectory, bool? telemetryEnabled )
    {
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
