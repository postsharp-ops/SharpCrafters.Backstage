// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;

namespace SharpCrafters.Backstage.Infrastructure;

internal sealed class ProcessExecutor : IProcessExecutor
{
    public IProcess Start( ProcessStartInfo startInfo )
    {
        ResetInheritedEnvironment( startInfo );

        return new ProcessWrapper( Process.Start( startInfo ) ?? throw new InvalidOperationException( "The process could not be started." ) );
    }

    public bool TryReadStandardOutput( ProcessStartInfo startInfo, TimeSpan timeout, [NotNullWhen( true )] out string? standardOutput )
    {
        startInfo.UseShellExecute = false;
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;
        startInfo.CreateNoWindow = true;

        ResetInheritedEnvironment( startInfo );

        standardOutput = null;

        var stopwatch = Stopwatch.StartNew();

        using var process = Process.Start( startInfo );

        if ( process == null )
        {
            return false;
        }

        // The output stream is read asynchronously. A blocking read returns only when the process closes the stream,
        // so it would make this method wait for the completion of the process for an unbounded time and the timeout
        // would have no effect.
        var outputTask = process.StandardOutput.ReadToEndAsync();

        // The error stream is drained, otherwise a full error buffer would block the child process while this method
        // reads its output stream.
        process.ErrorDataReceived += ( _, _ ) => { };
        process.BeginErrorReadLine();

        if ( !process.WaitForExit( GetRemainingMilliseconds( timeout, stopwatch ) ) )
        {
            Terminate( process );

            return false;
        }

        // The process closes its output stream when it exits, so the read completes, but it can complete after
        // WaitForExit has returned.
        if ( !outputTask.Wait( GetRemainingMilliseconds( timeout, stopwatch ) ) )
        {
            return false;
        }

        if ( process.ExitCode != 0 )
        {
            return false;
        }

        standardOutput = outputTask.Result;

        return true;
    }

    public async Task<string?> ReadStandardOutputAsync( ProcessStartInfo startInfo, TimeSpan timeout, CancellationToken cancellationToken )
    {
        startInfo.UseShellExecute = false;
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;
        startInfo.CreateNoWindow = true;

        ResetInheritedEnvironment( startInfo );

        cancellationToken.ThrowIfCancellationRequested();

        var stopwatch = Stopwatch.StartNew();

        using var process = Process.Start( startInfo );

        if ( process == null )
        {
            return null;
        }

        // The output stream is read asynchronously, for the reason given in TryReadStandardOutput: a blocking read
        // would wait for the completion of the process for an unbounded time.
        var outputTask = process.StandardOutput.ReadToEndAsync();

        // The error stream is drained, otherwise a full error buffer would block the child process while this method
        // reads its output stream.
        process.ErrorDataReceived += ( _, _ ) => { };
        process.BeginErrorReadLine();

        // The process is terminated both when the timeout expires and when the caller cancels, so that it does not
        // keep running after this method has given up on its output. This is the same reasoning as the one given on
        // Terminate for the synchronous path.
        using var timeoutSource = new CancellationTokenSource( timeout );
        using var waitSource = CancellationTokenSource.CreateLinkedTokenSource( timeoutSource.Token, cancellationToken );

        try
        {
            await WaitForExitAsync( process, waitSource.Token );
        }
        catch ( OperationCanceledException )
        {
            Terminate( process );

            // A cancellation asked by the caller is reported, while an expired timeout is a failure like any other
            // and is reported by a null result, exactly as in TryReadStandardOutput.
            cancellationToken.ThrowIfCancellationRequested();

            return null;
        }

        // The process closes its output stream when it exits, so the read completes, but it can complete after the
        // wait has returned.
        var remaining = GetRemainingMilliseconds( timeout, stopwatch );

        // The delay is cancelled once the read has completed. Without that, every call would leave a timer running
        // until the whole timeout had elapsed, although nothing was waiting for it any more.
        using var remainingSource = CancellationTokenSource.CreateLinkedTokenSource( cancellationToken );

        var completed = await Task.WhenAny( outputTask, Task.Delay( remaining, remainingSource.Token ) );

        remainingSource.Cancel();

        if ( completed != outputTask )
        {
            cancellationToken.ThrowIfCancellationRequested();

            return null;
        }

        if ( process.ExitCode != 0 )
        {
            return null;
        }

        return await outputTask;
    }

    /// <summary>
    /// Waits for the exit of a process without blocking the calling thread.
    /// </summary>
    private static Task WaitForExitAsync( Process process, CancellationToken cancellationToken )
    {
#if NET5_0_OR_GREATER
        return process.WaitForExitAsync( cancellationToken );
#else

        // Process.WaitForExitAsync does not exist before .NET 5, so the Exited event is used instead. The completion
        // source is asynchronous, otherwise the continuations of the awaiting caller would run on the thread that
        // raises the event, which belongs to the process component.
        var taskCompletionSource = new TaskCompletionSource<bool>( TaskCreationOptions.RunContinuationsAsynchronously );

        process.EnableRaisingEvents = true;
        process.Exited += ( _, _ ) => taskCompletionSource.TrySetResult( true );

        // The process can have exited between its start and the subscription above, in which case the event has
        // already been raised and would never be raised again.
        if ( process.HasExited )
        {
            taskCompletionSource.TrySetResult( true );
        }

        if ( !cancellationToken.CanBeCanceled )
        {
            return taskCompletionSource.Task;
        }

        return WaitWithCancellationAsync( taskCompletionSource.Task, cancellationToken );
#endif
    }

#if !NET5_0_OR_GREATER
    private static async Task WaitWithCancellationAsync( Task exited, CancellationToken cancellationToken )
    {
        var cancelled = new TaskCompletionSource<bool>( TaskCreationOptions.RunContinuationsAsynchronously );

        using ( cancellationToken.Register( () => cancelled.TrySetCanceled( cancellationToken ) ) )
        {
            await await Task.WhenAny( exited, cancelled.Task );
        }
    }
#endif

    /// <summary>
    /// Gets the part of the timeout that has not elapsed yet, in milliseconds, or zero when the timeout has expired.
    /// </summary>
    private static int GetRemainingMilliseconds( TimeSpan timeout, Stopwatch stopwatch )
    {
        var remaining = timeout - stopwatch.Elapsed;

        if ( remaining <= TimeSpan.Zero )
        {
            return 0;
        }

        return (int) Math.Min( remaining.TotalMilliseconds, int.MaxValue );
    }

    /// <summary>
    /// Terminates a process that has exceeded its timeout. Disposing the <see cref="Process"/> object closes the
    /// handle that this process holds, but it does not stop the child process, so a child process that is not
    /// terminated here would keep running after the caller has given up on it.
    /// </summary>
    private static void Terminate( Process process )
    {
        try
        {
            process.Kill();
        }
        catch ( Exception )
        {
            // The child process can exit between the expiration of the timeout and this call, and the caller has
            // already given up on its output, so a failure to terminate it is not reported.
        }
    }

    private static void ResetInheritedEnvironment( ProcessStartInfo startInfo )
    {
        if ( !startInfo.UseShellExecute )
        {
            // Reset a few environment variables set by the Visual Studio process.
            startInfo.Environment["DOTNET_MULTILEVEL_LOOKUP"] = "";
            startInfo.Environment["DOTNET_ROOT"] = "";
            startInfo.Environment["DOTNET_STARTUP_HOOKS"] = "";
            startInfo.Environment["DOTNET_TC_CallCountThreshold"] = "";
        }
        else
        {
            // We can't set environment variables with ShellExecute=true and this is also probably useless.
        }
    }

    private sealed class ProcessWrapper : IProcess
    {
        private readonly Process _process;

        public int ExitCode => this._process.ExitCode;

        public ProcessWrapper( Process process )
        {
            this._process = process;
            process.Exited += this.OnExited;
        }

        private void OnExited( object? sender, EventArgs e )
        {
            this.Exited?.Invoke();
        }

        public event Action? Exited;

        public bool HasExited => this._process.HasExited;

        public void WaitForExit() => this._process.WaitForExit();

        public void Dispose()
        {
            this._process.Exited -= this.OnExited;
            this._process.Dispose();
        }
    }
}
