// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using Metalama.Backstage;
using SharpCrafters.Backstage.Testing;
using SharpCrafters.Backstage.Threading;
using Xunit;

namespace SharpCrafters.Backstage.PlatformTests.NamedLocks;

/// <summary>
/// Runs <see cref="NamedLockService"/> against the named mutex of the operating system, contended by another process.
/// The unit tests cover the logic of the service within one process; these tests cover what the operating system does.
/// </summary>
public sealed class NamedLockTests
{
    private readonly NamedLockService _service = NamedLockServiceFactory.Create( MetalamaProduct.Profile.GlobalLockNamePrefix );

    private readonly List<LockEventKind> _events = [];

    private readonly TaskCompletionSource _blocked = new( TaskCreationOptions.RunContinuationsAsynchronously );

    // The name of a machine-wide lock, as the product names them, and unique to the test.
    private readonly string _name = $"{MetalamaProduct.Profile.GlobalLockNamePrefix}PlatformTests_{Guid.NewGuid():N}";

    public NamedLockTests()
    {
        this._service.LockEventReported += ( _, e ) =>
        {
            lock ( this._events )
            {
                this._events.Add( e.Kind );
            }

            if ( e.Kind == LockEventKind.Blocked )
            {
                this._blocked.TrySetResult();
            }
        };
    }

    /// <summary>
    /// A wait with a token that can be cancelled is the wait that failed on Linux and macOS, where the operating system
    /// cannot wait on a named mutex and a cancellation handle together.
    /// </summary>
    [PlatformFact( TestPlatforms.All )]
    public async Task ACancellableWaitAcquiresTheLockThatAnotherProcessReleases()
    {
        using var helper = HelperProcess.Start( HelperCommands.HoldLock, this._name );
        await helper.ReadUntilAsync( HelperCommands.AcquiredLine );

        using var namedLock = this._service.GetLock( this._name );
        using var cancellation = new CancellationTokenSource();

        var acquisition = Task.Run( () => namedLock.Acquire( HelperProcess.Timeout, cancellation.Token ) );

        // The helper releases the lock only once this process is waiting for it, so that the wait is contended.
        await this._blocked.Task.WaitAsync( HelperProcess.Timeout );
        helper.Signal();

        using var releaser = await acquisition.WaitAsync( HelperProcess.Timeout );

        this.AssertEvents( LockEventKind.Blocked, LockEventKind.Acquired );
        this.AssertNoEvent( LockEventKind.Degraded );
    }

    /// <summary>
    /// The operating system reports the abandonment to a process that has the mutex open when its owner terminates. When no
    /// process has it open, the mutex ceases to exist, on Windows as on Unix, and the next process creates a new one.
    /// </summary>
    [PlatformFact( TestPlatforms.All )]
    public async Task ALockOfAKilledProcessIsAcquiredAsAbandoned()
    {
        using var helper = HelperProcess.Start( HelperCommands.AbandonLock, this._name );
        await helper.ReadUntilAsync( HelperCommands.AcquiredLine );

        // Opens the mutex, which the helper owns, before the helper terminates.
        using var namedLock = this._service.GetLock( this._name );
        Assert.False( namedLock.TryAcquire( TimeSpan.Zero, out _ ) );

        helper.Kill();

        Assert.True( namedLock.TryAcquire( HelperProcess.Timeout, out var releaser ) );
        releaser.Dispose();

        this.AssertEvents( LockEventKind.Abandoned );
        this.AssertNoEvent( LockEventKind.Degraded );
    }

    private void AssertEvents( params LockEventKind[] expected )
    {
        lock ( this._events )
        {
            foreach ( var kind in expected )
            {
                Assert.Contains( kind, this._events );
            }
        }
    }

    private void AssertNoEvent( LockEventKind kind )
    {
        lock ( this._events )
        {
            Assert.DoesNotContain( kind, this._events );
        }
    }
}
