// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using Metalama.Backstage;
using Metalama.Backstage.Metalama;
using Microsoft.Extensions.DependencyInjection;
using SharpCrafters.Backstage.Configuration;
using SharpCrafters.Backstage.Extensibility;
using SharpCrafters.Backstage.Infrastructure;
using SharpCrafters.Backstage.Licensing;
using SharpCrafters.Backstage.Licensing.Consumption;
using SharpCrafters.Backstage.Licensing.Licenses;
using SharpCrafters.Backstage.Licensing.Registration;
using SharpCrafters.Backstage.Testing;
using System.Security.Cryptography;
using System.Text;

namespace SharpCrafters.Backstage.LicenseServerLoadSimulator;

/// <summary>
/// One installation of the product, belonging to one simulated user on one simulated machine.
/// </summary>
/// <remarks>
/// <para>
/// The point of this class is that the licensing services it holds are <b>the ones the product ships</b>. Nothing
/// here builds a lease request, parses an answer or decides when to renew: the simulation drives
/// <see cref="ILicenseConsumptionService"/>, and what reaches the server is what a customer's build would send. A
/// harness with a client of its own measures the load of that client, and goes on passing after the product has
/// changed underneath it.
/// </para>
/// <para>
/// Several installations coexist in one process because a service provider is what makes an installation: this one
/// answers with the account, the machine, the clock and the stored configuration of its own simulated user. The
/// configuration is in memory, so the run leaves the profile of the person running it untouched, and each simulated
/// user really does start from an empty one.
/// </para>
/// </remarks>
internal sealed class SimulatedInstallation : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly string _licenseServerUrl;

    public SimulatedInstallation(
        SimulationOptions options,
        IDateTimeProvider clock,
        IHttpClientFactory httpClientFactory,
        SimulatedUser user,
        string machineName )
    {
        this.UserName = user.UserName;
        this.MachineName = machineName;
        this._licenseServerUrl = options.Url;

        var applicationInfo = new TestApplicationInfo(
            "License Server Load Simulator",
            false,
            options.Version,

            // A build date inside the subscription period of whatever the server leases. A server refuses a request
            // whose build date is past the end of the subscription, which would look like a denial of capacity.
            new DateTime( 2026, 1, 15, 0, 0, 0, DateTimeKind.Utc ) )
        {
            // A simulated developer, not a build server: an unattended process never leases, so a simulation that
            // declared itself unattended would send nothing at all and would report a server that is never used.
            IsUnattendedProcess = false,

            // The audit uploads a report of the licenses it consumes to PostSharp Technologies. A simulation consumes
            // licenses nobody bought, on machines that do not exist, so it has nothing to report. The audit manager
            // also requires the support services, which this harness does not compose.
            IsLicenseAuditEnabled = false
        };

        var services = new ServiceCollection();

        var builder = new ServiceProviderBuilder(
            ( type, factory ) => services.AddSingleton( type, provider => factory( provider ) ),
            ( type, instance ) => services.AddSingleton( type, instance ) );

        builder
            .AddCoreServices( new CoreInitializationOptions( MetalamaProduct.Profile, applicationInfo ) )
            .AddConfigurationServices()
            .AddLicensingServices(
                new LicensingInitializationOptions
                {
                    // The catalog names the products whose license keys this installation consumes. The umbrella
                    // package of a product sets it; this harness composes the services itself and so has to name it.
                    ProductCatalog = MetalamaProduct.LicenseProductCatalog,

                    // A development license server signs the license keys it issues to itself, and a client that does
                    // not know that authority refuses every lease the server grants. Trusting it is what lets the
                    // simulation run against a server that nobody bought a license for.
                    AuthorityProviderFactory = options.TestAuthority is { } testAuthority
                        ? _ => new ExplicitLicensingAuthorityProvider( (testAuthority.KeyId, testAuthority.PublicKey) )
                        : serviceProvider => new ProductionLicensingAuthorityProvider( serviceProvider )
                },
                applicationInfo )

            // Registering a license server is something a person does, and the product refuses it in an unattended
            // session. The simulation says so itself rather than letting the answer depend on the machine it happens
            // to run on: a run from a remote session would otherwise register nothing and measure nothing.
            .AddUserDeviceDetection( new TestUserDeviceDetectionService { IsInteractiveDevice = true } );

        // Everything that makes this installation somebody else's. It is registered after the product, because the
        // last registration of a service wins.
        services.AddSingleton( clock );
        services.AddSingleton( httpClientFactory );
        services.AddSingleton<IUserIdentityProvider>( new TestUserIdentityProvider { UserName = user.UserName, MachineName = machineName } );
        services.AddSingleton<IMachineIdProvider>( new TestMachineIdProvider { MachineId = MachineIdOf( machineName ) } );
        services.AddSingleton<IConfigurationManager>( serviceProvider => new InMemoryConfigurationManager( serviceProvider ) );

        this._serviceProvider = services.BuildServiceProvider();
    }

    public string UserName { get; }

    public string MachineName { get; }

    /// <summary>
    /// Registers the license server, which is what the developer of this installation did once, before the first
    /// build. The registration contacts the server, so it takes the first seat.
    /// </summary>
    public async Task<string?> RegisterAsync( CancellationToken cancellationToken )
    {
        var result = await this._serviceProvider.GetRequiredBackstageService<ILicenseRegistrationService>()
            .RegisterLicenseAsync( this._licenseServerUrl, cancellationToken );

        return result.IsSuccess ? null : result.ErrorMessage;
    }

    /// <summary>
    /// Builds once, as a compilation of this installation would: the consumer resolves every licence, which renews
    /// the lease when it is due and uses the stored one when it is not, and a requirement then consumes it.
    /// </summary>
    /// <returns>The reason the build is not licensed, or <see langword="null"/> when it is.</returns>
    public async Task<string?> BuildAsync( CancellationToken cancellationToken )
    {
        var messages = new List<string>();

        var consumer = await this._serviceProvider.GetRequiredBackstageService<ILicenseConsumptionService>()
            .CreateConsumerAsync( null, message => messages.Add( message.Text ), cancellationToken );

        if ( consumer.TryConsume( LicenseRequirement.Any, message => messages.Add( message.Text ), false ) )
        {
            return null;
        }

        return messages.Count == 0 ? "No license." : string.Join( " ", messages );
    }

    /// <summary>
    /// Invents a stable machine identifier for a simulated machine. A real one comes from the operating system; what
    /// matters to a license server is only that two machines differ and that one machine keeps its own.
    /// </summary>
    private static string MachineIdOf( string machineName )
    {
        // A hash rather than a random value, so that a second run of the same simulation reuses the machines of the
        // first and the server accounts them as the same ones.
        var hash = SHA256.HashData( Encoding.UTF8.GetBytes( machineName ) );

        return new Guid( hash.AsSpan( 0, 16 ) ).ToString();
    }

    public void Dispose() => this._serviceProvider.Dispose();
}
