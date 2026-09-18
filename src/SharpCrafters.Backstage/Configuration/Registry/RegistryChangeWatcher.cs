// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;
using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace SharpCrafters.Backstage.Configuration.Registry;

/// <summary>
/// Calls back when a registry key, or anything below it, changes.
/// </summary>
/// <remarks>
/// <para>
/// This is the registry counterpart of the file watcher that the file-based configuration manager uses, and it
/// exists for the same reason: a long-running process has to notice a setting that another process changed. Here
/// the other process is usually another version of the product.
/// </para>
/// <para>
/// The notification of the registry is one-shot: it fires once and has to be asked for again. It is also not a
/// description of what changed, only that something did, so the caller re-reads what it cares about.
/// </para>
/// </remarks>
#pragma warning disable CA1416 // The registry is reached only where IsSupported reports one.
internal sealed class RegistryChangeWatcher : IDisposable
{
    private const int _errorSuccess = 0;

    private const int _notifyChangeName = 0x1;
    private const int _notifyChangeLastSet = 0x4;

    /// <summary>
    /// Asks for the notification to outlive the thread that registered it.
    /// </summary>
    /// <remarks>
    /// Without this flag the notification is removed when the thread that asked for it ends, which is exactly what
    /// happens here: the request is made on a thread-pool thread, which is returned to the pool immediately. The
    /// watch would then stop after the first change and the process would go quiet without any sign of it. The flag
    /// exists since Windows 8.
    /// </remarks>
    private const int _notifyThreadAgnostic = 0x10000000;

    private const int _notifyFilter = _notifyChangeName | _notifyChangeLastSet | _notifyThreadAgnostic;

    [DllImport( "advapi32.dll", SetLastError = true )]
    private static extern int RegNotifyChangeKeyValue(
        SafeRegistryHandle hKey,
        [MarshalAs( UnmanagedType.Bool )] bool watchSubtree,
        int notifyFilter,
        SafeWaitHandle hEvent,
        [MarshalAs( UnmanagedType.Bool )] bool asynchronous );

    private readonly RegistryKey _key;
    private readonly ManualResetEvent _changed = new( false );
    private readonly Action _onChanged;
    private RegisteredWaitHandle? _registration;
    private int _isDisposed;

    private RegistryChangeWatcher( RegistryKey key, Action onChanged )
    {
        this._key = key;
        this._onChanged = onChanged;
    }

    /// <summary>
    /// Starts watching a key.
    /// </summary>
    /// <param name="key">The key, which the watcher takes ownership of and disposes.</param>
    /// <param name="onChanged">Called after a change.</param>
    /// <returns>The watcher, or <see langword="null"/> when the key cannot be watched.</returns>
    public static RegistryChangeWatcher? Create( RegistryKey key, Action onChanged )
    {
        var watcher = new RegistryChangeWatcher( key, onChanged );

        if ( !watcher.Arm() )
        {
            watcher.Dispose();

            return null;
        }

        // Registered after the first request, so that the callback cannot run against a watcher that is not armed.
        watcher._registration = ThreadPool.RegisterWaitForSingleObject(
            watcher._changed,
            ( state, _ ) => ((RegistryChangeWatcher) state!).OnChanged(),
            watcher,
            Timeout.Infinite,
            false );

        return watcher;
    }

    /// <summary>
    /// Asks for the next notification.
    /// </summary>
    /// <returns><see langword="false"/> if the notification could not be requested, in which case the watch stops.</returns>
    private bool Arm()
    {
        if ( this._isDisposed != 0 )
        {
            return false;
        }

        try
        {
            return RegNotifyChangeKeyValue( this._key.Handle, true, _notifyFilter, this._changed.SafeWaitHandle, true ) == _errorSuccess;
        }
        catch ( Exception e ) when ( e is ObjectDisposedException or EntryPointNotFoundException or DllNotFoundException )
        {
            return false;
        }
    }

    private void OnChanged()
    {
        if ( this._isDisposed != 0 )
        {
            return;
        }

        // Reset and ask again before the callback runs, so that a change made while it runs is not lost. The
        // notification is one-shot, so a change arriving between the reset and the request would otherwise fall
        // between the two.
        this._changed.Reset();

        if ( !this.Arm() )
        {
            return;
        }

        try
        {
            this._onChanged();
        }
        catch
        {
            // A handler that throws must not bring down the thread-pool thread, and there is nothing to report it
            // to: this class has no logger, and its caller is the one that knows how to log.
        }
    }

    public void Dispose()
    {
        if ( Interlocked.Exchange( ref this._isDisposed, 1 ) != 0 )
        {
            return;
        }

        // Unregistered first, so that no callback can run against the handles that are about to be closed.
        this._registration?.Unregister( null );
        this._key.Dispose();
        this._changed.Dispose();
    }
}
#pragma warning restore CA1416
