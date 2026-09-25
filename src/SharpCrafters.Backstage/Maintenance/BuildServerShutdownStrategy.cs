// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Diagnostics;
using SharpCrafters.Backstage.Extensibility;
using SharpCrafters.Backstage.Infrastructure;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel;
using System.Diagnostics;

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

    protected override ImmutableArray<KillableProcessSpec> ProcessSpecs { get; } = ImmutableArray.Create(
        new KillableProcessSpec( _compilerServerName, KillableModuleKind.Both, true, true ),
        new KillableProcessSpec( "MSBuild", KillableModuleKind.Both, false, true ) );

    protected override void OnProcessesFound( ProcessShutdownOptions options, IReadOnlyList<KillableProcess> processes )
    {
        if ( processes.Count == 0 )
        {
            return;
        }

        // Found before the request, so that the servers that exit on it are reported too.
        try
        {
            if ( !this._processExecutor.TryExecute( new ProcessStartInfo( "dotnet", "build-server shutdown" ), options.Timeout, out _ ) )
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

    protected override ProcessShutdownResult ShutDownProcess( KillableProcess process, ProcessShutdownOptions options, Stopwatch stopwatch )
    {
        var isCompilerServer = string.Equals( process.Spec.Name, _compilerServerName, StringComparison.OrdinalIgnoreCase );
        var description = isCompilerServer ? "Compiler server (VBCSCompiler)" : "MSBuild node";

        if ( isCompilerServer && process.Shutdown() )
        {
            return new ProcessShutdownResult( description, process.Process.Id, ProcessShutdownOutcome.Exited );
        }

        if ( options.Force )
        {
            // An MSBuild node has no shutdown request of its own, and ending it is what the option asks for.
            return Kill( process, description );
        }

        if ( process.Process.WaitForExit( (int) GetRemainingTime( options, stopwatch ).TotalMilliseconds ) )
        {
            return new ProcessShutdownResult( description, process.Process.Id, ProcessShutdownOutcome.Exited );
        }

        var reason = isCompilerServer
            ? "it did not exit in time; its compilations may still be running; use --force to end it"
            : "an MSBuild node exits when it has been idle for some minutes; use --force to end it";

        return new ProcessShutdownResult( description, process.Process.Id, ProcessShutdownOutcome.StillRunning, reason );
    }
}
