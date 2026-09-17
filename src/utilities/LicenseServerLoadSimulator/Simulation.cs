// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

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
/// so often until the evening, and renews its lease when the one it holds is due. A day of the virtual clock takes a
/// real minute at the default acceleration of a license server, so a fortnight of leases, renewals and expiries fits
/// into a coffee break.
/// </para>
/// <para>
/// What it exercises that a unit test cannot: the real socket, the real server, its database, its global lock under
/// concurrency, and its seat accounting over time.
/// </para>
/// </remarks>
internal sealed class Simulation
{
    private readonly SimulationOptions _options;
    private readonly AcceleratedDateTimeProvider _clock;
    private readonly LeaseClient _leaseClient;
    private readonly ConcurrentDictionary<string, int> _outcomes = new( StringComparer.Ordinal );

    private int _activeRequests;
    private int _peakActiveRequests;

    public Simulation( SimulationOptions options, AcceleratedDateTimeProvider clock, LeaseClient leaseClient )
    {
        this._options = options;
        this._clock = clock;
        this._leaseClient = leaseClient;
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
        LicenseLeaseInfo? lease = null;

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

                    for ( var time = startOfDay; time < endOfDay; time = time.AddMinutes( 30 * random.NextDouble() ) )
                    {
                        await this._clock.WaitUntilAsync( time, cancellationToken );

                        if ( cancellationToken.IsCancellationRequested )
                        {
                            return;
                        }

                        // A build only contacts the server when the lease it holds is due for renewal, which is what
                        // the product does.
                        if ( lease != null && lease.RenewTime > this._clock.UtcNow )
                        {
                            continue;
                        }

                        lease = await this.AcquireLeaseAsync( user, machine, lease, cancellationToken );
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

    private async Task<LicenseLeaseInfo?> AcquireLeaseAsync(
        SimulatedUser user,
        string machine,
        LicenseLeaseInfo? previousLease,
        CancellationToken cancellationToken )
    {
        var active = Interlocked.Increment( ref this._activeRequests );
        InterlockedMax( ref this._peakActiveRequests, active );

        var startedAt = DateTime.UtcNow;

        try
        {
            var result = await this._leaseClient.TryGetLeaseAsync( user.UserName, machine, cancellationToken );

            if ( result.Lease == null )
            {
                this.Record( "denied or failed" );
                Console.WriteLine( $"{this.Now} {user.UserName} on {machine}: no lease. {result.ErrorMessage}" );

                return previousLease;
            }

            this.Record( previousLease == null ? "first lease" : "renewal" );

            // A lease whose renewal instant has already passed means the clock of this process has run ahead of the
            // server's. The round trip is not compensated, so the drift is real and grows; re-anchoring is the only
            // remedy the protocol offers.
            if ( result.Lease.RenewTime < this._clock.UtcNow )
            {
                this.Record( "clock re-synchronized" );

                Console.WriteLine(
                    $"{this.Now} {user.UserName}: the lease is already due for renewal at {result.Lease.RenewTime:u}. Re-synchronizing the clock." );

                await this._clock.SyncAsync( cancellationToken );
            }

            var elapsed = DateTime.UtcNow - startedAt;

            if ( elapsed > TimeSpan.FromSeconds( 1 ) )
            {
                this.Record( "slow response" );
                Console.WriteLine( $"{this.Now} {user.UserName}: the server answered in {elapsed.TotalSeconds:F1} s." );
            }

            return result.Lease;
        }
        catch ( OperationCanceledException )
        {
            throw;
        }
        catch ( Exception e )
        {
            this.Record( "exception" );
            Console.WriteLine( $"{this.Now} {user.UserName}: {e.GetType().Name}: {e.Message}" );

            return previousLease;
        }
        finally
        {
            Interlocked.Decrement( ref this._activeRequests );
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
                    $"{this.Now} -- {this._activeRequests} request(s) in flight, peak {this._peakActiveRequests}; "
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
        Console.WriteLine( $"Peak concurrent requests: {this._peakActiveRequests}" );

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

            // A build server works every day, which is what makes it worth separating from the developers.
            users.Add( new SimulatedUser( $"CONTOSO\\BUILDSERVER.{i}", machines, 1, true ) );
        }

        return users.ToImmutable();
    }
}
