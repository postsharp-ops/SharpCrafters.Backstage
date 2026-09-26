// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Diagnostics;
using SharpCrafters.Backstage.Extensibility;
using SharpCrafters.Backstage.Infrastructure;
using SharpCrafters.Backstage.Utilities;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;

namespace SharpCrafters.Backstage.Maintenance;

/// <summary>
/// Stops the build servers that keep the assemblies of a build loaded: the MSBuild nodes and the compiler server.
/// </summary>
/// <remarks>
/// <para>
/// The build servers of the .NET SDK are asked to exit with <c>dotnet build-server shutdown</c>, which reaches its MSBuild
/// nodes, its compiler server and its Razor server. The compiler server of Visual Studio is asked with
/// <c>VBCSCompiler -shutdown</c>, which lets its running compilations end. The MSBuild nodes of Visual Studio have no
/// such request: they exit when they have been idle for some minutes, and are otherwise ended with
/// <see cref="ProcessShutdownOptions.Force"/>, which also ends a node that is running a build.
/// </para>
/// </remarks>
internal sealed class BuildServerShutdownStrategy : SpecifiedProcessShutdownStrategy
{
    private const string _compilerServerName = "VBCSCompiler";

    private readonly IProcessExecutor _processExecutor;
    private readonly ILogger _logger;

    public BuildServerShutdownStrategy( IServiceProvider serviceProvider ) : base( serviceProvider )
    {
        this._processExecutor = serviceProvider.GetRequiredBackstageService<IProcessExecutor>();
        this._logger = serviceProvider.GetLoggerFactory().GetLogger( "ProcessShutdown" );
    }

    protected override ImmutableArray<ProcessSpec> ProcessSpecs { get; } = ImmutableArray.Create(
        new ProcessSpec( _compilerServerName, ProcessModuleKind.Both ),
        new ProcessSpec( "MSBuild", ProcessModuleKind.Both ) );

    protected override IReadOnlyList<ProcessShutdownResult> ShutDown( IReadOnlyList<MatchedProcess> processes, ProcessShutdownOptions options )
    {
        if ( processes.Count == 0 )
        {
            return [];
        }

        // One timeout for the whole procedure, which is what the user asked to wait. Every wait below takes the part of
        // it that remains, so that the waits do not add up.
        var stopwatch = Stopwatch.StartNew();

        // The build servers of the .NET SDK first. The processes were found before, so that the ones that exit on the
        // request are reported as well.
        this.RequestDotNetBuildServerShutdown( options.Timeout );

        return processes.Select( process => this.ShutDown( process, options, stopwatch ) ).ToList();
    }

    private void RequestDotNetBuildServerShutdown( TimeSpan timeout )
    {
        try
        {
            if ( !this._processExecutor.TryExecute( new ProcessStartInfo( "dotnet", "build-server shutdown" ), timeout, out _ ) )
            {
                this._logger.Warning?.Log( "'dotnet build-server shutdown' did not complete successfully." );
            }
        }
        catch ( Exception e ) when ( e is Win32Exception or InvalidOperationException )
        {
            // The .NET SDK is not installed. Its build servers then do not exist either.
            this._logger.Trace?.Log( $"Cannot run 'dotnet build-server shutdown': {e.Message}" );
        }
    }

    /// <param name="stopwatch">Measures the time spent since the procedure started, against <see cref="ProcessShutdownOptions.Timeout"/>.</param>
    private ProcessShutdownResult ShutDown( MatchedProcess process, ProcessShutdownOptions options, Stopwatch stopwatch )
    {
        var isCompilerServer = string.Equals( process.Spec.Name, _compilerServerName, StringComparison.OrdinalIgnoreCase );
        var description = isCompilerServer ? "Compiler server (VBCSCompiler)" : "MSBuild node";

        if ( isCompilerServer && this.RequestShutdown( process, options.Timeout, stopwatch ) )
        {
            return new ProcessShutdownResult( description, process.Process.Id, ProcessShutdownOutcome.Exited );
        }

        if ( options.Force )
        {
            // An MSBuild node has no shutdown request of its own, and ending it is what the option asks for.
            return this.Kill( process, description );
        }

        if ( process.Process.WaitForExit( ProcessExecutor.GetRemainingMilliseconds( options.Timeout, stopwatch ) ) )
        {
            return new ProcessShutdownResult( description, process.Process.Id, ProcessShutdownOutcome.Exited );
        }

        var reason = isCompilerServer
            ? "it did not exit in time; its compilations may still be running; use --force to end it"
            : "an MSBuild node exits when it has been idle for some minutes; use --force to end it";

        return new ProcessShutdownResult( description, process.Process.Id, ProcessShutdownOutcome.StillRunning, reason );
    }

    /// <summary>
    /// Asks a compiler server to shut down, as <c>VBCSCompiler -shutdown</c> does: the same executable, or the same
    /// assembly under <c>dotnet</c>, run with <c>-shutdown</c>, lets the compilations of the server end and then stops it.
    /// </summary>
    /// <returns><c>true</c> when the server has exited before <paramref name="timeout"/> has elapsed on
    /// <paramref name="stopwatch"/>.</returns>
    private bool RequestShutdown( MatchedProcess match, TimeSpan timeout, Stopwatch stopwatch )
    {
        var process = match.Process;

        try
        {
            if ( process.HasExited )
            {
                return true;
            }

            this._logger.Trace?.Log( $"Asking the compiler server {process.Id} to shut down." );

            var arguments = match.MainModule != null ? new[] { match.MainModule, "-shutdown" } : new[] { "-shutdown" };
            var startInfo = new ProcessStartInfo( process.MainModule!.FileName, CommandLineArguments.Format( arguments ) );

            // Bounded like every other wait of the procedure: the executor ends the request when the time is up.
            var remaining = TimeSpan.FromMilliseconds( ProcessExecutor.GetRemainingMilliseconds( timeout, stopwatch ) );

            if ( !this._processExecutor.TryExecute( startInfo, remaining, out _ ) )
            {
                this._logger.Trace?.Log( $"The shutdown request to the compiler server {process.Id} did not complete successfully." );
            }

            return process.WaitForExit( ProcessExecutor.GetRemainingMilliseconds( timeout, stopwatch ) );
        }
        catch ( Exception e ) when ( e is Win32Exception or InvalidOperationException )
        {
            this._logger.Warning?.Log( $"Could not ask the compiler server {process.Id} to shut down: {e.Message}" );

            return false;
        }
    }
}
