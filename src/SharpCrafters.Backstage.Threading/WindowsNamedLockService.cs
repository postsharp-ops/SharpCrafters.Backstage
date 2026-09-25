// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Common.Testing.Hooks;

namespace SharpCrafters.Backstage.Threading;

/// <summary>
/// The implementation of <see cref="NamedLockService"/> on Windows. It creates the mutex with a security descriptor
/// that lets every user use it, and it waits on the mutex and on the token together.
/// </summary>
internal sealed class WindowsNamedLockService : NamedLockService
{
    /// <summary>
    /// Whether to pass a security descriptor when creating a mutex. It is latched to <see langword="false"/> the
    /// first time the platform rejects one, so that the cost is paid only once.
    /// </summary>
    private volatile bool _useAccessControl = true;

    /// <inheritdoc cref="NamedLockService(IServiceProvider)"/>
    public WindowsNamedLockService( IServiceProvider serviceProvider ) : base( serviceProvider ) { }

    /// <inheritdoc cref="NamedLockService(string, ITestSynchronizationProvider, ITestFaultInjector)"/>
    public WindowsNamedLockService(
        string globalLockNamePrefix,
        ITestSynchronizationProvider? testSynchronizationProvider = null,
        ITestFaultInjector? testFaultInjector = null )
        : base( globalLockNamePrefix, testSynchronizationProvider, testFaultInjector ) { }

    /// <inheritdoc />
    private protected override Mutex CreateMutex( string name )
    {
        if ( this._useAccessControl )
        {
            try
            {
                // Creating a mutex without a security descriptor uses the default security, which can make the object
                // unusable by another user.
                this.InjectFault( BeforeCreateWithAccessControlLocation, name );

                var securedMutex = MutexAcl.Create( false, name, MutexAcl.AllowUsingMutexToEveryone );

                this.Report( LockEventKind.Created, name );

                return securedMutex;
            }
            catch ( PlatformNotSupportedException e )
            {
                this._useAccessControl = false;

                if ( this.IsReportEnabled )
                {
                    this.Report( LockEventKind.Created, name, detail: $"security descriptors are unavailable: {e.Message}" );
                }
            }
        }

        this.InjectFault( BeforeCreateLocation, name );

        var mutex = new Mutex( false, name );

        this.Report( LockEventKind.Created, name, detail: "created without a security descriptor" );

        return mutex;
    }

    /// <inheritdoc />
    private protected override bool WaitCancellable( Mutex mutex, TimeSpan timeout, CancellationToken cancellationToken )
    {
        // Mutex.WaitOne has no cancellable overload, so the wait handle of the token is waited upon alongside the mutex
        // itself. The array is allocated for each wait, which is acceptable because this path is taken only when the
        // caller actually supplied a token, and only when the lock was found to be owned.
        var index = WaitHandle.WaitAny( new WaitHandle[] { mutex, cancellationToken.WaitHandle }, timeout );

        switch ( index )
        {
            case 0:
                return true;

            case 1:
                // The token was signalled first, so the mutex was not acquired and must not be released.
                throw new OperationCanceledException( cancellationToken );

            default:
                // WaitHandle.WaitTimeout.
                return false;
        }
    }
}
