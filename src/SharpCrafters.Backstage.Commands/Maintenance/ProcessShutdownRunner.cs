// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using Microsoft.Extensions.DependencyInjection;
using SharpCrafters.Backstage.Maintenance;
using System;
using System.Diagnostics;
using System.Linq;

namespace SharpCrafters.Backstage.Commands.Maintenance;

/// <summary>
/// Runs every registered <see cref="IProcessShutdownStrategy"/> and reports what each did, for the <c>shutdown</c> command,
/// also named <c>kill</c>, and the <c>cleanup</c> command.
/// </summary>
internal static class ProcessShutdownRunner
{
    /// <param name="timeout">How long all the strategies together may wait for their processes.</param>
    /// <param name="reportDevelopmentEnvironments">
    /// <c>false</c> to leave out of the report the processes that are only reported, which is what <c>--no-warn</c> asks.
    /// </param>
    /// <returns>The number of processes that may still be running.</returns>
    public static int Run( ExtendedCommandContext context, bool force, TimeSpan timeout, bool reportDevelopmentEnvironments )
    {
        var console = context.Console;

        // Resolved as a collection: every strategy contributes, see IProcessShutdownStrategy.
        var strategies = context.ServiceProvider.GetServices<IProcessShutdownStrategy>().ToList();
        var stopwatch = Stopwatch.StartNew();

        var stopped = 0;
        var running = 0;

        foreach ( var strategy in strategies )
        {
            var remaining = timeout - stopwatch.Elapsed;
            var options = new ProcessShutdownOptions( force, remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero );

            foreach ( var result in strategy.ShutDownProcesses( options ) )
            {
                var subject = result.ProcessId == 0 ? result.Description : $"{result.Description} (process {result.ProcessId})";

                switch ( result.Outcome )
                {
                    case ProcessShutdownOutcome.Exited:
                        console.WriteSuccess( $"{subject}: exited." );
                        stopped++;

                        break;

                    case ProcessShutdownOutcome.Ended:
                        console.WriteSuccess( $"{subject}: ended." );
                        stopped++;

                        break;

                    case ProcessShutdownOutcome.StillRunning:
                        console.WriteWarning( $"{subject}: still running: {result.Reason}." );
                        running++;

                        break;

                    case ProcessShutdownOutcome.NotActedOn:
                        console.WriteWarning( $"{subject}: not acted on: {result.Reason}." );
                        running++;

                        break;

                    case ProcessShutdownOutcome.Reported:
                        if ( reportDevelopmentEnvironments )
                        {
                            console.WriteWarning( $"{subject}: left running: {result.Reason}." );
                        }

                        break;
                }
            }
        }

        if ( running == 0 )
        {
            console.WriteSuccess( stopped == 0 ? "No process had to be stopped." : $"{stopped} process{(stopped == 1 ? " was" : "es were")} stopped." );
        }

        return running;
    }
}
