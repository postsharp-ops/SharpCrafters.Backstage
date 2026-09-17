// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using System.Globalization;

namespace SharpCrafters.Backstage.LicenseServerLoadSimulator;

/// <summary>
/// What the simulation should do, as read from the command line.
/// </summary>
internal sealed record SimulationOptions
{
    public string Url { get; init; } = "http://localhost:44670/";

    /// <summary>
    /// Gets the number of developers, each of whom works a day, builds every so often and renews their lease.
    /// </summary>
    public int UserCount { get; init; } = 75;

    /// <summary>
    /// Gets the number of build servers, whose leases a real server does not store and which consume no seat.
    /// </summary>
    public int BuildServerCount { get; init; } = 2;

    /// <summary>
    /// Gets the number of machines each build server builds on.
    /// </summary>
    public int BuildServerMachineCount { get; init; } = 2;

    /// <summary>
    /// Gets the product whose pool of licences the server should allocate from, or <see langword="null"/> for any.
    /// </summary>
    public string? Product { get; init; } = "MetalamaProfessional";

    /// <summary>
    /// Gets the version the simulated clients declare. A server reads an absent one as 4.9.9.
    /// </summary>
    public string Version { get; init; } = "2027.0.0";

    /// <summary>
    /// Gets how long the simulation runs in real time, after which it stops and prints its summary.
    /// </summary>
    public TimeSpan Duration { get; init; } = TimeSpan.FromMinutes( 10 );

    /// <summary>
    /// Gets the seed of the random number generator, so that a run can be repeated.
    /// </summary>
    public int Seed { get; init; }

    public static bool TryParse( string[] args, out SimulationOptions options, out string? errorMessage )
    {
        options = new SimulationOptions();
        errorMessage = null;

        for ( var i = 0; i < args.Length; i++ )
        {
            var argument = args[i];

            if ( !argument.StartsWith( "--", StringComparison.Ordinal ) )
            {
                // The first positional argument is the URL, as in the original harness.
                options = options with { Url = argument };

                continue;
            }

            if ( i + 1 >= args.Length )
            {
                errorMessage = $"The option '{argument}' takes a value.";

                return false;
            }

            var value = args[++i];

            switch ( argument )
            {
                case "--url":
                    options = options with { Url = value };

                    break;

                case "--users":
                    if ( !TryParseCount( argument, value, out var userCount, out errorMessage ) ) { return false; }

                    options = options with { UserCount = userCount };

                    break;

                case "--build-servers":
                    if ( !TryParseCount( argument, value, out var buildServerCount, out errorMessage ) ) { return false; }

                    options = options with { BuildServerCount = buildServerCount };

                    break;

                case "--product":
                    options = options with { Product = value.Length == 0 ? null : value };

                    break;

                case "--version":
                    options = options with { Version = value };

                    break;

                case "--minutes":
                    if ( !double.TryParse( value, NumberStyles.Float, CultureInfo.InvariantCulture, out var minutes ) || minutes <= 0 )
                    {
                        errorMessage = $"The option '{argument}' takes a positive number of minutes.";

                        return false;
                    }

                    options = options with { Duration = TimeSpan.FromMinutes( minutes ) };

                    break;

                case "--seed":
                    if ( !TryParseCount( argument, value, out var seed, out errorMessage ) ) { return false; }

                    options = options with { Seed = seed };

                    break;

                default:
                    errorMessage = $"Unknown option '{argument}'.";

                    return false;
            }
        }

        return true;
    }

    private static bool TryParseCount( string argument, string value, out int count, out string? errorMessage )
    {
        if ( !int.TryParse( value, NumberStyles.Integer, CultureInfo.InvariantCulture, out count ) || count < 0 )
        {
            errorMessage = $"The option '{argument}' takes a whole number.";

            return false;
        }

        errorMessage = null;

        return true;
    }

    public static string Usage
        => """
           Drives a license server with a simulated organization, on the accelerated clock of that server.

           Usage:
             LicenseServerLoadSimulator [url] [options]

           Options:
             --url <url>              The base URL of the license server. Default: http://localhost:44670/
             --users <n>              Number of developers. Default: 75
             --build-servers <n>      Number of build servers. Default: 2
             --product <name>         Product to lease, or empty for any. Default: MetalamaProfessional
             --version <version>      Version the clients declare. Default: 2027.0.0
             --minutes <n>            How long to run, in real minutes. Default: 10
             --seed <n>               Seed of the random number generator. Default: 0

           The server must be a Debug build with TimeAcceleration set in its web.config, and its application pool
           must be recycled between runs: its virtual clock is anchored when the pool starts and never resets.
           """;
}
