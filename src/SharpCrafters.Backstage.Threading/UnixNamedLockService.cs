// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Common.Testing.Hooks;
using System.Diagnostics;

namespace SharpCrafters.Backstage.Threading;

/// <summary>
/// The implementation of <see cref="NamedLockService"/> on Linux and macOS. It creates the mutex without a security
/// descriptor, and it waits for the mutex with a cancellation token in short slices.
/// </summary>
/// <remarks>
/// <para>
/// The runtime implements a named mutex on these systems with files under <c>/tmp/.dotnet/shm</c>, which have no
/// security descriptor. It does not wait on a named synchronization object together with another handle:
/// <see cref="WaitHandle.WaitAny(WaitHandle[], TimeSpan)"/> throws <see cref="PlatformNotSupportedException"/>.
/// </para>
/// <para>
/// Nothing in this class depends on Windows being absent, so a test can run it on Windows.
/// </para>
/// </remarks>
internal sealed class UnixNamedLockService : NamedLockService
{
    /// <summary>
    /// The longest time for which a cancellable wait on a mutex does not observe a cancellation.
    /// </summary>
    private const int _cancellationPollingIntervalMilliseconds = 100;

    /// <inheritdoc cref="NamedLockService(IServiceProvider)"/>
    public UnixNamedLockService( IServiceProvider serviceProvider ) : base( serviceProvider ) { }

    /// <inheritdoc cref="NamedLockService(string, ITestSynchronizationProvider, ITestFaultInjector)"/>
    public UnixNamedLockService(
        string globalLockNamePrefix,
        ITestSynchronizationProvider? testSynchronizationProvider = null,
        ITestFaultInjector? testFaultInjector = null )
        : base( globalLockNamePrefix, testSynchronizationProvider, testFaultInjector ) { }

    /// <inheritdoc />
    private protected override Mutex CreateMutex( string name )
    {
        this.InjectFault( BeforeCreateLocation, name );

        var mutex = new Mutex( false, name );

        this.Report( LockEventKind.Created, name );

        return mutex;
    }

    /// <inheritdoc />
    /// <remarks>
    /// The wait is a sequence of <see cref="WaitHandle.WaitOne(TimeSpan)"/> calls of at most
    /// <see cref="_cancellationPollingIntervalMilliseconds"/>, and the token is observed between two calls. A slice only
    /// bounds the time taken to observe a cancellation. It does not delay the acquisition, because
    /// <see cref="WaitHandle.WaitOne(TimeSpan)"/> returns as soon as the owner releases the mutex.
    /// </remarks>
    private protected override bool WaitCancellable( Mutex mutex, TimeSpan timeout, CancellationToken cancellationToken )
    {
        var slice = TimeSpan.FromMilliseconds( _cancellationPollingIntervalMilliseconds );
        var isInfinite = timeout == Timeout.InfiniteTimeSpan;
        var startTimestamp = Stopwatch.GetTimestamp();

        while ( true )
        {
            cancellationToken.ThrowIfCancellationRequested();

            var currentSlice = slice;

            if ( !isInfinite )
            {
                var remaining = timeout - GetElapsed( startTimestamp );

                if ( remaining < slice )
                {
                    currentSlice = remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
                }
            }

            if ( mutex.WaitOne( currentSlice ) )
            {
                return true;
            }

            if ( currentSlice < slice )
            {
                // The last slice was the rest of the timeout.
                return false;
            }
        }
    }
}
