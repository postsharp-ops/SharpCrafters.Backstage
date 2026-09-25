// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Diagnostics;
using SharpCrafters.Backstage.Extensibility;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;

namespace SharpCrafters.Backstage.Maintenance;

/// <summary>
/// The base of the strategies of this package, which find their processes from a list of <see cref="ProcessSpec"/>
/// and act on each one in turn.
/// </summary>
internal abstract class SpecifiedProcessShutdownStrategy : IProcessShutdownStrategy
{
    private readonly IProcessManager _processManager;

    protected SpecifiedProcessShutdownStrategy( IServiceProvider serviceProvider )
    {
        this._processManager = serviceProvider.GetRequiredBackstageService<IProcessManager>();
        this.Logger = serviceProvider.GetLoggerFactory().GetLogger( "ProcessShutdown" );
    }

    protected ILogger Logger { get; }

    /// <summary>
    /// Gets the processes that this strategy acts on.
    /// </summary>
    protected abstract ImmutableArray<ProcessSpec> ProcessSpecs { get; }

    /// <summary>
    /// Called once, before the processes are acted on, for instance to ask a build server to exit.
    /// </summary>
    protected virtual void OnProcessesFound( ProcessShutdownOptions options, IReadOnlyList<MatchedProcess> processes ) { }

    /// <summary>
    /// Acts on one process.
    /// </summary>
    /// <param name="stopwatch">
    /// Started when the first process was acted on. <see cref="GetRemainingTime"/> gives how long the strategy may still
    /// wait, so that the timeout applies to all the processes together.
    /// </param>
    protected abstract ProcessShutdownResult ShutDownProcess( MatchedProcess process, ProcessShutdownOptions options, Stopwatch stopwatch );

    public IReadOnlyList<ProcessShutdownResult> ShutDownProcesses( ProcessShutdownOptions options )
    {
        var results = new List<ProcessShutdownResult>();
        var candidates = this._processManager.GetCandidateProcesses( this.ProcessSpecs );

        try
        {
            // The process that runs the command and its parents are never selected, so that a tool of the product, or a
            // build that runs the command, survives it.
            var processes = this._processManager.GetMatchingProcesses( candidates, this.ProcessSpecs ).ToList();

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
    protected ProcessShutdownResult Kill( MatchedProcess match, string description )
    {
        var process = match.Process;

        try
        {
            if ( !process.HasExited )
            {
                this.Logger.Trace?.Log( $"Ending the process '{process.ProcessName}' ({process.Id})." );

                process.Kill();
                process.WaitForExit();
            }

            return new ProcessShutdownResult( description, process.Id, ProcessShutdownOutcome.Ended );
        }
        catch ( InvalidOperationException ) when ( process.HasExited )
        {
            // The process exited on its own meanwhile.
            return new ProcessShutdownResult( description, process.Id, ProcessShutdownOutcome.Exited );
        }
        catch ( Exception e )
        {
            this.Logger.Error?.Log( $"Could not end the process '{process.ProcessName}' ({process.Id}): {e.Message}" );

            return new ProcessShutdownResult( description, process.Id, ProcessShutdownOutcome.StillRunning, $"it could not be ended: {e.Message}" );
        }
    }
}
