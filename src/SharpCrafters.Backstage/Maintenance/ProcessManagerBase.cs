// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Application;
using SharpCrafters.Backstage.Diagnostics;
using SharpCrafters.Backstage.Extensibility;
using SharpCrafters.Backstage.ProcessClassification;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;

namespace SharpCrafters.Backstage.Maintenance;

/// <summary>
/// Finds the processes that match a list of <see cref="KillableProcessSpec"/>, for the implementations of
/// <see cref="IProcessShutdownStrategy"/>. The implementation differs by operating system in how it reads the modules
/// of a process.
/// </summary>
internal abstract class ProcessManagerBase : IProcessManager
{
    private const string _dotNetProcessName = "dotnet";

    protected ILogger Logger { get; }

    /// <summary>
    /// The profile of the product, which gives the prefix of the assemblies that identify a process as using the
    /// product.
    /// </summary>
    private readonly ProductProfile _productProfile;

    private readonly IParentProcessSearch _parentProcessSearch;

    protected ProcessManagerBase( IServiceProvider serviceProvider )
    {
        this._parentProcessSearch = serviceProvider.GetRequiredBackstageService<IParentProcessSearch>();
        this.Logger = serviceProvider.GetLoggerFactory().GetLogger( "ProcessManager" );
        this._productProfile = serviceProvider.GetRequiredBackstageService<ProductProfile>();
    }

    protected virtual bool TryGetModulePaths( Process process, [NotNullWhen( true )] out List<string>? modules )
    {
        modules = [];

        try
        {
            foreach ( ProcessModule module in process.Modules )
            {
                if ( module.FileName != null! )
                {
                    modules.Add( module.FileName );
                }
            }

            return true;
        }
        catch ( Exception e )
        {
            if ( !process.HasExited )
            {
                this.Logger.Warning?.Log( $"Cannot enumerate the modules of '{process.Id}': {e.Message}." );
            }

            return false;
        }
    }

    /// <summary>
    /// Determines whether a process has loaded the product, from the names of its modules.
    /// </summary>
    /// <returns><c>true</c> if a module of the process belongs to the product, or <c>null</c> when it cannot be determined.</returns>
    protected bool? ReferencesProduct( Process process, IReadOnlyList<string> modules )
    {
        var assemblyNamePrefix = this._productProfile.AssemblyNamePrefix;

        if ( modules.Any( m => Path.GetFileNameWithoutExtension( m ).StartsWith( assemblyNamePrefix, StringComparison.OrdinalIgnoreCase ) ) )
        {
            return true;
        }

        // TODO: Determines if the process references the product.
        // We cannot do it by looking at the modules because .NET assemblies are not always exposed as modules.

        this.Logger.Trace?.Log( $"Cannot determine if process '{process.ProcessName}' ({process.Id}) uses {this._productProfile.Name}." );

        return null;
    }

    /// <summary>
    /// Gets the processes that may match one of <paramref name="processSpecs"/>: the <c>dotnet</c> processes, and the
    /// processes named after a specification of a standalone process.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The caller owns the processes, and disposes all of them once it has acted on the ones that
    /// <see cref="GetKillableProcesses"/> selects.
    /// </para>
    /// <para>
    /// The standalone processes are enumerated on every operating system, and not on Windows alone, because the language
    /// server of the Visual Studio Code C# Dev Kit runs as its own executable on Linux and on macOS as well. The
    /// comparison of the process name is case insensitive, which is what <see cref="Process.GetProcessesByName(string)"/>
    /// performs on every platform.
    /// </para>
    /// </remarks>
#pragma warning disable CA1307
    public List<Process> GetCandidateProcesses( ImmutableArray<KillableProcessSpec> processSpecs )
    {
        var processes = new List<Process>();

        if ( processSpecs.Any( s => s.IsDotNet ) )
        {
            var dotnetProcesses = Process.GetProcessesByName( _dotNetProcessName );

            this.Logger.Trace?.Log( $"Found {dotnetProcesses.Length} 'dotnet' processes." );

            processes.AddRange( dotnetProcesses );
        }

        foreach ( var processSpec in processSpecs.Where( s => s.IsStandaloneProcess ) )
        {
            processes.AddRange( Process.GetProcessesByName( processSpec.Name.ToLowerInvariant() ) );
        }

        return processes;
    }
#pragma warning restore CA1307

    /// <summary>
    /// Selects, among <paramref name="candidates"/>, the processes that match one of <paramref name="processSpecs"/>, whether
    /// they run as an assembly under the <c>dotnet</c> process name or as their own executable.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The <see cref="KillableProcess"/> objects do not own their processes: the caller of <see cref="GetCandidateProcesses"/>
    /// disposes them.
    /// </para>
    /// <para>
    /// The current process and its parents are never selected: see <see cref="ExcludeCurrentProcessAndParents{T}"/>.
    /// </para>
    /// </remarks>
    public IEnumerable<KillableProcess> GetKillableProcesses( IEnumerable<Process> candidates, ImmutableArray<KillableProcessSpec> processSpecs )
    {
        foreach ( var process in this.ExcludeCurrentProcessAndParents( candidates, p => p.Id ) )
        {
            // The name comes from the snapshot that Process.GetProcessesByName took, so it is available after the process exits.
            var killableProcess = string.Equals( process.ProcessName, _dotNetProcessName, StringComparison.OrdinalIgnoreCase )
                ? this.SelectDotNetProcess( process, processSpecs )
                : this.SelectStandaloneProcess( process, processSpecs );

            if ( killableProcess != null )
            {
                yield return killableProcess;
            }
        }
    }

    private KillableProcess? SelectDotNetProcess( Process process, ImmutableArray<KillableProcessSpec> processSpecs )
    {
        if ( !this.TryGetModulePaths( process, out var modules ) )
        {
            this.Logger.Trace?.Log( $"Cannot get module paths for process {process.Id}." );

            return null;
        }

        this.Logger.Trace?.Log( $"Process {process.Id} modules: {string.Join( ", ", modules )}." );

        var moduleFileNames = modules.Select( s => Path.GetFileNameWithoutExtension( s ).ToLowerInvariant() ).ToList();

        foreach ( var processSpec in processSpecs )
        {
            if ( !processSpec.IsDotNet )
            {
                continue;
            }

            var moduleIndex = moduleFileNames.IndexOf( processSpec.Name.ToLowerInvariant() );

            if ( moduleIndex >= 0 )
            {
                var mainModule = modules[moduleIndex];

                if ( this.ReferencesProduct( process, modules ) == false )
                {
                    this.Logger.Trace?.Log( $"Do not kill '{process.ProcessName}' '{mainModule}' ({process.Id}) because it does not contain {this._productProfile.Name}." );

                    return null;
                }

                this.Logger.Trace?.Log( $"Process '{process.ProcessName}' '{mainModule}' ({process.Id}) should be killed." );

                return new KillableProcess( process, this.Logger, mainModule, processSpec );
            }
        }

        if ( this.ReferencesProduct( process, modules ) != false )
        {
            this.Logger.Trace?.Log(
                $"Do not kill '{process.ProcessName}' ({process.Id}) even if it references {this._productProfile.Name} because it is not a known process." );
        }

        return null;
    }

    private KillableProcess? SelectStandaloneProcess( Process process, ImmutableArray<KillableProcessSpec> processSpecs )
    {
        var processSpec = processSpecs.FirstOrDefault(
            s => s.IsStandaloneProcess && string.Equals( s.Name, process.ProcessName, StringComparison.OrdinalIgnoreCase ) );

        if ( processSpec == default )
        {
            return null;
        }

        if ( !this.TryGetModulePaths( process, out var modules ) )
        {
            return null;
        }

        if ( this.ReferencesProduct( process, modules ) == false )
        {
            this.Logger.Trace?.Log( $"Do not kill '{process.ProcessName}' ({process.Id}) because it does not contain {this._productProfile.Name}." );

            return null;
        }

        return new KillableProcess( process, this.Logger, null, processSpec );
    }

    /// <summary>
    /// Removes the current process and its parent processes from the processes to stop.
    /// </summary>
    /// <param name="processes">The processes to stop.</param>
    /// <param name="getProcessId">Gets the identifier of a process.</param>
    /// <remarks>
    /// <para>
    /// A process that asks for the shutdown must survive it, and so must the processes that wait for it. The parent of
    /// the current process is often one of the processes to stop: <c>dotnet build</c> and <c>dotnet test</c> run MSBuild
    /// in their own process, and so does a <c>dotnet</c> tool started from a build. Stopping a parent would end the
    /// operation that asked for the shutdown.
    /// </para>
    /// <para>
    /// When the parent processes cannot be determined, only the current process is excluded, and a warning is logged.
    /// </para>
    /// </remarks>
    internal IEnumerable<T> ExcludeCurrentProcessAndParents<T>( IEnumerable<T> processes, Func<T, int> getProcessId )
    {
#if NET
        var excludedProcessIds = new HashSet<int> { Environment.ProcessId };
#else
        var excludedProcessIds = new HashSet<int> { Process.GetCurrentProcess().Id };
#endif

        try
        {
            foreach ( var parent in this._parentProcessSearch.GetParentProcesses() )
            {
                excludedProcessIds.Add( parent.ProcessId );
            }
        }
        catch ( Exception e )
        {
            this.Logger.Warning?.Log( $"Cannot determine the parent processes, so only the current process is excluded from the shutdown: {e.Message}" );
        }

        foreach ( var process in processes )
        {
            var processId = getProcessId( process );

            if ( excludedProcessIds.Contains( processId ) )
            {
                this.Logger.Trace?.Log( $"Do not stop the process {processId}, because it is the current process or one of its parents." );

                continue;
            }

            yield return process;
        }
    }
}
