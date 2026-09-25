// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Diagnostics;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SharpCrafters.Backstage.ProcessClassification;

/// <summary>
/// Attaches a debugger to the current process when a user asks for one.
/// </summary>
/// <remarks>
/// <para>
/// Where a just-in-time debugger can be launched, which is on Windows outside a container, the methods of this class
/// call <see cref="Debugger.Launch"/>. Elsewhere <see cref="Debugger.Launch"/> returns without attaching anything,
/// so the methods report the process identifier and wait until a debugger is attached by hand.
/// </para>
/// <para>
/// The environment variable <c>HAS_JIT_DEBUGGER</c> overrides that decision. It accepts <c>0</c>, <c>1</c>,
/// <c>true</c> and <c>false</c>, and any other value is ignored. The name is not prefixed with a product name,
/// because PostSharp read the same variable before it used this class.
/// </para>
/// <para>
/// This class is in a package with no dependency, so that an MSBuild task, which starts no service, can use it.
/// </para>
/// </remarks>
public static class DebuggerHelper
{
    private const string _hasJustInTimeDebuggerEnvironmentVariable = "HAS_JIT_DEBUGGER";

    private static readonly object _sync = new();
    private static bool _launchOnceRequested;

    /// <summary>
    /// Attaches a debugger to the current process, unless one is attached already. The request is made again at
    /// each call.
    /// </summary>
    /// <param name="onWaitingForDebugger">
    /// Called with the process identifier before waiting for a debugger to be attached by hand, so that the caller
    /// can report it through its own output. When <c>null</c>, the message is written to the standard error stream.
    /// It is not called when a just-in-time debugger is launched.
    /// </param>
    /// <param name="cancellationToken">A token that ends the wait for a debugger.</param>
    /// <exception cref="OperationCanceledException">The wait was cancelled before a debugger was attached.</exception>
    public static void Launch( Action<int>? onWaitingForDebugger = null, CancellationToken cancellationToken = default )
    {
        lock ( _sync )
        {
            LaunchCore( onWaitingForDebugger, cancellationToken );
        }
    }

    /// <summary>
    /// Attaches a debugger to the current process the first time it is called in the process, unless one is attached
    /// already. Later calls do nothing, even if the user refused the first request or detached the debugger since.
    /// </summary>
    /// <remarks>
    /// This is the form for a request that comes from configuration, which is read more than once in a process:
    /// one request makes a better debugging experience than one at each read.
    /// </remarks>
    public static void LaunchOnce()
    {
        lock ( _sync )
        {
            if ( _launchOnceRequested )
            {
                return;
            }

            _launchOnceRequested = true;

            LaunchCore( null, CancellationToken.None );
        }
    }

    /// <summary>
    /// Gets a value indicating whether <see cref="Debugger.Launch"/> can attach a just-in-time debugger to the
    /// current process.
    /// </summary>
    public static bool CanLaunchJustInTimeDebugger
        => CanLaunchJustInTimeDebuggerCore(
            Environment.GetEnvironmentVariable( _hasJustInTimeDebuggerEnvironmentVariable ),
            RuntimeInformation.IsOSPlatform( OSPlatform.Windows ),
            ContainerDetection.IsRunningInContainer );

    /// <summary>
    /// Decides whether a just-in-time debugger can be launched.
    /// </summary>
    /// <param name="environmentVariableValue">The value of <c>HAS_JIT_DEBUGGER</c>, or <c>null</c>.</param>
    /// <param name="isWindows">Whether the process runs on Windows.</param>
    /// <param name="isRunningInContainer">
    /// Determines whether the process runs inside a container. It is only called when the answer is needed, because
    /// on Linux it reads files.
    /// </param>
    /// <remarks>The inputs are parameters, so that a test can state the rule on any machine.</remarks>
    internal static bool CanLaunchJustInTimeDebuggerCore( string? environmentVariableValue, bool isWindows, Func<ILogger?, bool> isRunningInContainer )
    {
        if ( environmentVariableValue != null )
        {
            if ( int.TryParse( environmentVariableValue, out var intValue ) )
            {
                return intValue != 0;
            }

            if ( bool.TryParse( environmentVariableValue, out var boolValue ) )
            {
                return boolValue;
            }
        }

        // A just-in-time debugger is only available on Windows, and a Windows container has none.
        return isWindows && !isRunningInContainer( null );
    }

    private static void LaunchCore( Action<int>? onWaitingForDebugger, CancellationToken cancellationToken )
    {
        if ( Debugger.IsAttached )
        {
            return;
        }

        if ( CanLaunchJustInTimeDebugger )
        {
            Debugger.Launch();

            return;
        }

        var processId = Process.GetCurrentProcess().Id;

        if ( onWaitingForDebugger != null )
        {
            onWaitingForDebugger( processId );
        }
        else
        {
            Console.Error.WriteLine( $"Waiting until a debugger is attached to the process {processId}." );
        }

        while ( !Debugger.IsAttached )
        {
            cancellationToken.WaitHandle.WaitOne( 200 );
            cancellationToken.ThrowIfCancellationRequested();
        }
    }
}
