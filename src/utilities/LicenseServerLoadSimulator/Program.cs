// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Infrastructure;
using SharpCrafters.Backstage.LicenseServerLoadSimulator;
using SharpCrafters.Backstage.Testing;

if ( args.Length == 1 && args[0] is "--help" or "-h" or "/?" )
{
    Console.WriteLine( SimulationOptions.Usage );

    return 0;
}

if ( !SimulationOptions.TryParse( args, out var options, out var errorMessage ) )
{
    Console.Error.WriteLine( errorMessage );
    Console.Error.WriteLine();
    Console.Error.WriteLine( SimulationOptions.Usage );

    return 1;
}

var httpClientFactory = new SimpleHttpClientFactory();
var clock = new AcceleratedDateTimeProvider( httpClientFactory, options.Url );

// The clock is read from the server before anything else: every instant of the simulation is on the virtual clock of
// that server, and without this the simulation would run at real speed and wait for days that never come.
try
{
    await clock.SyncAsync();
}
catch ( Exception e )
{
    Console.Error.WriteLine( $"Cannot read the clock of the license server at '{options.Url}': {e.Message}" );
    Console.Error.WriteLine();

    Console.Error.WriteLine(
        "The server must answer GetTime.ashx. Check that it is running, and that it is a Debug build if you expect "
        + "its clock to be accelerated." );

    return 1;
}

if ( clock.Acceleration <= 1 )
{
    Console.Error.WriteLine(
        $"The license server reports an acceleration of {clock.Acceleration}, so its clock runs at real speed and this "
        + "simulation would take weeks. Set TimeAcceleration in the web.config of the server, and build it in Debug: "
        + "the acceleration is compiled out of a Release build." );

    return 1;
}

using var cancellationTokenSource = new CancellationTokenSource( options.Duration );

Console.CancelKeyPress += ( _, e ) =>
{
    e.Cancel = true;
    cancellationTokenSource.Cancel();
};

using var simulation = new Simulation( options, clock, httpClientFactory );

await simulation.RunAsync( cancellationTokenSource.Token );

simulation.PrintSummary();

return 0;

/// <summary>
/// The factory of HTTP clients of this tool, which has no service provider of its own.
/// </summary>
internal sealed class SimpleHttpClientFactory : IHttpClientFactory
{
    public HttpClient Create() => this.Create( HttpClientOptions.Default );

    public HttpClient Create( HttpClientOptions options )
    {
        var client = options.UseDefaultCredentials
            ? new HttpClient( new HttpClientHandler { UseDefaultCredentials = true } )
            : new HttpClient();

        if ( options.Timeout != null )
        {
            client.Timeout = options.Timeout.Value;
        }

        return client;
    }
}
