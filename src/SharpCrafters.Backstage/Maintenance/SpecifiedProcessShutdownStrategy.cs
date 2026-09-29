// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Diagnostics;
using SharpCrafters.Backstage.Extensibility;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace SharpCrafters.Backstage.Maintenance;

/// <summary>
/// The base of the strategies of this package, which find their processes from a list of <see cref="ProcessSpec"/>.
/// </summary>
/// <remarks>
/// The base class finds the processes, gives them to <see cref="ShutDown"/>, and disposes them. How they are stopped is
/// the whole of what a derived strategy decides.
/// </remarks>
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
    /// Stops the processes that this strategy found.
    /// </summary>
    /// <param name="processes">
    /// The processes that match <see cref="ProcessSpecs"/>, except the current process and its parents, and except the
    /// processes whose modules cannot be read, which the base class reports itself. The list is empty when none is
    /// running. The base class disposes the processes.
    /// </param>
    /// <returns>One result per process.</returns>
    protected abstract IReadOnlyList<ProcessShutdownResult> ShutDown( IReadOnlyList<MatchedProcess> processes, ProcessShutdownOptions options );

    public IReadOnlyList<ProcessShutdownResult> ShutDownProcesses( ProcessShutdownOptions options )
    {
        // The process that runs the command and its parents are never selected, so that a tool of the product, or a build
        // that runs the command, survives it.
        var processes = this._processManager.GetMatchingProcesses( this.ProcessSpecs );

        try
        {
            var accessible = processes.Where( p => p.InaccessibleReason == null ).ToList();

            var inaccessible = processes
                .Where( p => p.InaccessibleReason != null )
                .Select( p => new ProcessShutdownResult( p.Process.ProcessName, p.Process.Id, ProcessShutdownOutcome.NotActedOn, p.InaccessibleReason ) );

            return this.ShutDown( accessible, options ).Concat( inaccessible ).ToList();
        }
        finally
        {
            foreach ( var process in processes )
            {
                process.Process.Dispose();
            }
        }
    }

    /// <summary>
    /// The shortest wait for a process that has been ended. Ending a process is asynchronous but completes almost at once, so
    /// a timeout that has already expired must not make a process that was ended read as still running.
    /// </summary>
    private static readonly TimeSpan _minimalWaitAfterKill = TimeSpan.FromSeconds( 5 );

    /// <summary>
    /// Ends a process and reports the result.
    /// </summary>
    /// <param name="timeout">How long to wait for the process to exit once it has been ended, at least five seconds.</param>
    protected ProcessShutdownResult Kill( MatchedProcess match, string description, TimeSpan timeout )
    {
        var process = match.Process;

        try
        {
            if ( process.HasExited )
            {
                return new ProcessShutdownResult( description, process.Id, ProcessShutdownOutcome.Exited );
            }

            this.Logger.Trace?.Log( $"Ending the process '{process.ProcessName}' ({process.Id})." );

            process.Kill();

            var wait = timeout > _minimalWaitAfterKill ? timeout : _minimalWaitAfterKill;

            if ( !process.WaitForExit( (int) Math.Min( wait.TotalMilliseconds, int.MaxValue ) ) )
            {
                return new ProcessShutdownResult(
                    description,
                    process.Id,
                    ProcessShutdownOutcome.StillRunning,
                    "it was ended but has not exited in time" );
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
