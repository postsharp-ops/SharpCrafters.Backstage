// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Extensibility;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;

namespace SharpCrafters.Backstage.Maintenance;

/// <summary>
/// The base of the strategies of this package, which find their processes from a list of <see cref="KillableProcessSpec"/>
/// and act on each one in turn.
/// </summary>
internal abstract class SpecifiedProcessShutdownStrategy : IProcessShutdownStrategy
{
    private readonly IProcessManager _processManager;

    protected SpecifiedProcessShutdownStrategy( IServiceProvider serviceProvider )
    {
        this._processManager = serviceProvider.GetRequiredBackstageService<IProcessManager>();
    }

    /// <summary>
    /// Gets the processes that this strategy acts on.
    /// </summary>
    protected abstract ImmutableArray<KillableProcessSpec> ProcessSpecs { get; }

    /// <summary>
    /// Called once, before the processes are acted on, for instance to ask a build server to exit.
    /// </summary>
    protected virtual void OnProcessesFound( ProcessShutdownOptions options, IReadOnlyList<KillableProcess> processes ) { }

    /// <summary>
    /// Acts on one process.
    /// </summary>
    /// <param name="stopwatch">
    /// Started when the first process was acted on. <see cref="GetRemainingTime"/> gives how long the strategy may still
    /// wait, so that the timeout applies to all the processes together.
    /// </param>
    protected abstract ProcessShutdownResult ShutDownProcess( KillableProcess process, ProcessShutdownOptions options, Stopwatch stopwatch );

    public IReadOnlyList<ProcessShutdownResult> ShutDownProcesses( ProcessShutdownOptions options )
    {
        var results = new List<ProcessShutdownResult>();
        var candidates = this._processManager.GetCandidateProcesses( this.ProcessSpecs );

        try
        {
            using var currentProcess = Process.GetCurrentProcess();
            var processes = new List<KillableProcess>();

            foreach ( var process in this._processManager.GetKillableProcesses( candidates, this.ProcessSpecs ) )
            {
                // The process that runs the command is never acted on, so that a tool of the product can run it too.
                if ( process.Process.Id != currentProcess.Id )
                {
                    processes.Add( process );
                }
            }

            this.OnProcessesFound( options, processes );

            var stopwatch = Stopwatch.StartNew();

            foreach ( var process in processes )
            {
                results.Add( this.ShutDownProcess( process, options, stopwatch ) );
            }
        }
        finally
        {
            foreach ( var candidate in candidates )
            {
                candidate.Dispose();
            }
        }

        return results;
    }

    /// <summary>
    /// Gets the time that remains before <see cref="ProcessShutdownOptions.Timeout"/> has elapsed since
    /// <paramref name="stopwatch"/> was started.
    /// </summary>
    protected static TimeSpan GetRemainingTime( ProcessShutdownOptions options, Stopwatch stopwatch )
    {
        var remaining = options.Timeout - stopwatch.Elapsed;

        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }

    /// <summary>
    /// Ends a process and reports the result.
    /// </summary>
    protected static ProcessShutdownResult Kill( KillableProcess process, string description )
        => process.Kill( out var errorMessage )
            ? new ProcessShutdownResult( description, process.Process.Id, ProcessShutdownOutcome.Ended )
            : new ProcessShutdownResult( description, process.Process.Id, ProcessShutdownOutcome.StillRunning, $"it could not be ended: {errorMessage}" );
}
