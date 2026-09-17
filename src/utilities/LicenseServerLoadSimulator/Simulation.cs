// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Infrastructure;
using SharpCrafters.Backstage.Testing;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Globalization;

namespace SharpCrafters.Backstage.LicenseServerLoadSimulator;

/// <summary>
/// Drives a license server with a simulated organization, on the accelerated clock of that server.
/// </summary>
/// <remarks>
/// <para>
/// Every user runs on its own task and lives a working day on the virtual clock: it starts around eight, builds every
/// so often until the evening, and its installation of the product decides for itself whether a build has to contact
/// the server. A day of the virtual clock takes a real minute at the default acceleration of a license server, so a
/// fortnight of leases, renewals and expiries fits into a coffee break.
/// </para>
/// <para>
/// What it exercises that a unit test cannot: the real socket, the real server, its database, its global lock under
/// concurrency, and its seat accounting over time. What it exercises that a harness with a client of its own cannot:
/// the licensing code that customers actually run, including when it decides to say nothing.
/// </para>
/// </remarks>
internal sealed class Simulation : IDisposable
{
    private readonly SimulationOptions _options;
    private readonly AcceleratedDateTimeProvider _clock;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ConcurrentDictionary<string, int> _outcomes = new( StringComparer.Ordinal );
    private readonly List<SimulatedInstallation> _installations = new();

    private int _activeBuilds;
    private int _peakActiveBuilds;

    public Simulation( SimulationOptions options, AcceleratedDateTimeProvider clock, IHttpClientFactory httpClientFactory )
    {
        this._options = options;
        this._clock = clock;
        this._httpClientFactory = httpClientFactory;
    }

    public async Task RunAsync( CancellationToken cancellationToken )
    {
        var users = this.CreateUsers();

        Console.WriteLine(
            $"Simulating {users.Length} users ({users.Count( u => u.IsBuildServer )} build servers) against {this._options.Url}." );

        Console.WriteLine(
            $"The clock of the server runs {this._clock.Acceleration} times faster than real time, so one real minute is "
            + $"{TimeSpan.FromMinutes( (double) this._clock.Acceleration ):d\\ hh\\:mm} of virtual time." );

        Console.WriteLine();

        var printer = this.PrintProgressAsync( cancellationToken );

        await Task.WhenAll( users.Select( user => this.SimulateUserAsync( user, cancellationToken ) ) );
        await printer;
    }

    /// <summary>
    /// Lives the working life of one user until the simulation stops.
    /// </summary>
    private async Task SimulateUserAsync( SimulatedUser user, CancellationToken cancellationToken )
    {
        // One generator per user, seeded from the user, so that a run with the same seed replays the same working
        // pattern although the tasks interleave differently.
        var random = new Random( this._options.Seed ^ user.UserName.GetHashCode( StringComparison.Ordinal ) );

        // One installation of the product per machine this user works on, as in a real organization.
        var installations = new Dictionary<string, SimulatedInstallation>( StringComparer.Ordinal );

        try
        {
            while ( !cancellationToken.IsCancellationRequested )
            {
                var day = this._clock.UtcNow.Date;

                // Nobody starts at exactly the same time.
                var startOfDay = day.AddHours( 8 + ((random.NextDouble() - 0.5) * 4) );
                await this._clock.WaitUntilAsync( startOfDay, cancellationToken );

                var worksToday = day.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)
                                 || random.NextDouble() < user.WeekendProbability;

                if ( worksToday )
                {
                    var endOfDay = startOfDay.AddHours( 9 + random.NextDouble() );

                    // Which machine does this user work on today?
                    var machine = user.Machines[random.Next( user.Machines.Length )];
                    var installation = await this.GetInstallationAsync( installations, user, machine, cancellationToken );

                    for ( var time = startOfDay; time < endOfDay; time = time.AddMinutes( 30 * random.NextDouble() ) )
                    {
                        await this._clock.WaitUntilAsync( time, cancellationToken );

                        if ( cancellationToken.IsCancellationRequested )
                        {
                            return;
                        }

                        // Whether this build contacts the server at all is the decision of the product, not of the
                        // harness. That decision is a large part of what a load simulation is measuring.
                        await this.BuildAsync( installation, cancellationToken );
                    }
                }

                // Sleep until the next day rather than spinning on a day nobody worked.
                await this._clock.WaitUntilAsync( day.AddDays( 1 ), cancellationToken );
            }
        }
        catch ( OperationCanceledException )
        {
            // The simulation reached its duration.
        }
    }

    /// <summary>
    /// Gets the installation of a user on a machine, setting it up the first time they work on it: that is the
    /// moment a developer registers the license server of their organization, and it takes their first seat.
    /// </summary>
    private async Task<SimulatedInstallation> GetInstallationAsync(
        Dictionary<string, SimulatedInstallation> installations,
        SimulatedUser user,
        string machine,
        CancellationToken cancellationToken )
    {
        if ( installations.TryGetValue( machine, out var existingInstallation ) )
        {
            return existingInstallation;
        }

        var installation = new SimulatedInstallation( this._options, this._clock, this._httpClientFactory, user, machine );

        lock ( this._installations )
        {
            this._installations.Add( installation );
        }

        installations.Add( machine, installation );

        var errorMessage = await installation.RegisterAsync( cancellationToken );

        if ( errorMessage == null )
        {
            this.Record( "registered" );
        }
        else
        {
            this.Record( "registration refused" );
            Console.WriteLine( $"{this.Now} {user.UserName} on {machine}: could not register the license server. {errorMessage}" );
        }

        return installation;
    }

    private async Task BuildAsync( SimulatedInstallation installation, CancellationToken cancellationToken )
    {
        var active = Interlocked.Increment( ref this._activeBuilds );
        InterlockedMax( ref this._peakActiveBuilds, active );

        var startedAt = DateTime.UtcNow;

        try
        {
            var errorMessage = await installation.BuildAsync( cancellationToken );

            if ( errorMessage == null )
            {
                this.Record( "licensed build" );
            }
            else
            {
                this.Record( "unlicensed build" );
                Console.WriteLine( $"{this.Now} {installation.UserName} on {installation.MachineName}: {errorMessage}" );
            }

            var elapsed = DateTime.UtcNow - startedAt;

            if ( elapsed > TimeSpan.FromSeconds( 1 ) )
            {
                this.Record( "slow build" );
                Console.WriteLine( $"{this.Now} {installation.UserName}: the build waited {elapsed.TotalSeconds:F1} s for the license server." );
            }
        }
        catch ( OperationCanceledException )
        {
            throw;
        }
        catch ( Exception e )
        {
            this.Record( "exception" );
            Console.WriteLine( $"{this.Now} {installation.UserName}: {e.GetType().Name}: {e.Message}" );
        }
        finally
        {
            Interlocked.Decrement( ref this._activeBuilds );
        }
    }

    private async Task PrintProgressAsync( CancellationToken cancellationToken )
    {
        try
        {
            while ( !cancellationToken.IsCancellationRequested )
            {
                await Task.Delay( TimeSpan.FromSeconds( 15 ), cancellationToken );

                Console.WriteLine(
                    $"{this.Now} -- {this._activeBuilds} build(s) in flight, peak {this._peakActiveBuilds}; "
                    + string.Join( ", ", this._outcomes.OrderBy( o => o.Key, StringComparer.Ordinal ).Select( o => $"{o.Key}: {o.Value}" ) ) );
            }
        }
        catch ( OperationCanceledException )
        {
            // The simulation reached its duration.
        }
    }

    public void PrintSummary()
    {
        Console.WriteLine();
        Console.WriteLine( "Summary" );
        Console.WriteLine( "-------" );
        Console.WriteLine( $"Virtual time reached: {this._clock.UtcNow:u}" );
        Console.WriteLine( $"Clock synchronizations: {this._clock.SyncCount}" );
        Console.WriteLine( $"Simulated installations: {this._installations.Count}" );
        Console.WriteLine( $"Peak concurrent builds: {this._peakActiveBuilds}" );

        foreach ( var outcome in this._outcomes.OrderBy( o => o.Key, StringComparer.Ordinal ) )
        {
            Console.WriteLine( $"{outcome.Key}: {outcome.Value}" );
        }
    }

    private string Now => this._clock.UtcNow.ToString( "u", CultureInfo.InvariantCulture );

    private void Record( string outcome ) => this._outcomes.AddOrUpdate( outcome, 1, ( _, count ) => count + 1 );

    private static void InterlockedMax( ref int target, int value )
    {
        int current;

        while ( (current = Volatile.Read( ref target )) < value )
        {
            if ( Interlocked.CompareExchange( ref target, value, current ) == current )
            {
                return;
            }
        }
    }

    /// <summary>
    /// Builds the simulated organization: developers with one or two machines, and build servers whose leases a real
    /// server does not store.
    /// </summary>
    private ImmutableArray<SimulatedUser> CreateUsers()
    {
        var random = new Random( this._options.Seed );
        var users = ImmutableArray.CreateBuilder<SimulatedUser>();

        for ( var i = 0; i < this._options.UserCount; i++ )
        {
            // Most developers build on one machine; the rest have a desktop and a laptop, which is what makes the
            // rule of a real server, that a user may hold several machines within one seat, worth exercising.
            var machines = random.NextDouble() < 0.7
                ? ImmutableArray.Create( $"DESKTOP-{random.Next( 0, ushort.MaxValue ):x}" )
                : ImmutableArray.Create( $"DESKTOP-{random.Next( 0, ushort.MaxValue ):x}", $"NOTEBOOK-{random.Next( 0, ushort.MaxValue ):x}" );

            users.Add( new SimulatedUser( $"CONTOSO\\USER.{i:D3}", machines, random.NextDouble(), false ) );
        }

        for ( var i = 0; i < this._options.BuildServerCount; i++ )
        {
            var machines = ImmutableArray.CreateRange(
                Enumerable.Range( 0, this._options.BuildServerMachineCount ).Select( _ => $"SERVER-{random.Next( 0, ushort.MaxValue ):x}" ) );

            // A build server works every day, which is what makes it worth separating from the developers. It runs
            // the product attended here, because an unattended process never leases at all: what is being simulated
            // is a machine that builds constantly, not the rule that keeps a real one away from the server.
            users.Add( new SimulatedUser( $"CONTOSO\\BUILDSERVER.{i}", machines, 1, true ) );
        }

        return users.ToImmutable();
    }

    public void Dispose()
    {
        foreach ( var installation in this._installations )
        {
            installation.Dispose();
        }
    }
}
