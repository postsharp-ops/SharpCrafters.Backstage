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

internal abstract partial class ProcessManagerBase : IProcessManager
{
    /// <summary>
    /// The processes that every product stops or reports. The tool applications of the product are added by the
    /// constructor, because their names depend on the product.
    /// </summary>
    private static readonly ImmutableArray<KillableProcessSpec> _commonProcessesToKill = ImmutableArray.Create(
        new KillableProcessSpec( "VBCSCompiler", KillableModuleKind.Both, true, true ),
        new KillableProcessSpec( "MSBuild", KillableModuleKind.Both, false, true ),
        new KillableProcessSpec( "servicehub.roslyncodeanalysisservice", KillableModuleKind.Both, false, false, "Visual Studio" ),

        // Visual Studio 2026 runs the Roslyn analysis process under this name instead. See issue #1463. Like the
        // process that it replaces, it is reported to the user rather than stopped, because Visual Studio owns it.
        // Ending a child process of the integrated development environment leaves that environment in an
        // inconsistent state, and Visual Studio starts the analysis process again as soon as a document is opened,
        // which would lock the files again before the clean-up finished.
        new KillableProcessSpec( "devhub", KillableModuleKind.Both, false, false, "Visual Studio" ),
        new KillableProcessSpec( "jetbrains.resharper.roslyn.worker", KillableModuleKind.DotNet, false, false, "Rider/Resharper" ),
        new KillableProcessSpec( "jetbrains.roslyn.worker", KillableModuleKind.DotNet, false, false, "Rider/Resharper" ),
        new KillableProcessSpec( "omnisharp", KillableModuleKind.DotNet, false, false, "Visual Studio Code / Omnisharp" ),

        // The language server of the Visual Studio Code C# Dev Kit. It runs either as its own executable or as an
        // assembly under 'dotnet', so it is matched as both kinds of module. Like OmniSharp above, which is the
        // language server that preceded it, it is reported to the user rather than stopped, because Visual Studio
        // Code owns it and starts it again.
        new KillableProcessSpec(
            "microsoft.codeanalysis.languageserver",
            KillableModuleKind.Both,
            false,
            false,
            "Visual Studio Code / C# Dev Kit" ) );

    /// <summary>
    /// The common processes followed by the tool applications of the product.
    /// </summary>
    private readonly ImmutableArray<KillableProcessSpec> _processesToKill;

    /// <summary>
    /// The specifications of the tool applications of the product, which are also the last items of
    /// <see cref="_processesToKill"/>, with the tool that each one matches.
    /// </summary>
    private readonly ImmutableArray<(KillableProcessSpec Spec, Tools.BackstageTool Tool)> _toolProcesses;

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

        this._toolProcesses = ImmutableArray.Create(
            // The Backstage Worker runs under 'dotnet' (hosting the worker assembly), so it is matched as a DotNet module.
            (new KillableProcessSpec( Tools.BackstageTool.Worker.GetAssemblyName( this._productProfile ), KillableModuleKind.DotNet, false, true ),
             Tools.BackstageTool.Worker),

            // The Backstage Desktop tray app is a standalone '.exe'.
            (new KillableProcessSpec(
                 Tools.BackstageTool.DesktopWindows.GetAssemblyName( this._productProfile ),
                 KillableModuleKind.StandaloneProcess,
                 false,
                 true ),
             Tools.BackstageTool.DesktopWindows) );

        this._processesToKill = _commonProcessesToKill.AddRange( this._toolProcesses.Select( t => t.Spec ) );
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
    /// Returns the <see cref="KillableProcess"/> that <paramref name="select"/> creates for each of <paramref name="processes"/>,
    /// and disposes each process for which it returns <c>null</c>.
    /// </summary>
    /// <remarks>
    /// Each process is either owned by the <see cref="KillableProcess"/> returned to the caller, or disposed here. When the
    /// caller stops the enumeration early, or <paramref name="select"/> throws, the processes that remain are disposed too.
    /// </remarks>
    private static IEnumerable<KillableProcess> SelectOrDispose( Process[] processes, Func<Process, KillableProcess?> select )
    {
        // The index of the first process that has been neither returned nor disposed.
        var next = 0;

        try
        {
            while ( next < processes.Length )
            {
                var process = processes[next];
                var killableProcess = select( process );
                next++;

                if ( killableProcess == null )
                {
                    process.Dispose();
                }
                else
                {
                    yield return killableProcess;
                }
            }
        }
        finally
        {
            for ( var i = next; i < processes.Length; i++ )
            {
                processes[i].Dispose();
            }
        }
    }

    protected IEnumerable<KillableProcess> GetDotNetProcesses( ImmutableArray<KillableProcessSpec> processSpecs )
    {
        var dotnetProcesses = Process.GetProcessesByName( "dotnet" );

        this.Logger.Trace?.Log( $"Found {dotnetProcesses.Length} 'dotnet' processes." );

        return SelectOrDispose( dotnetProcesses, process => this.SelectDotNetProcess( process, processSpecs ) );
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

    /// <summary>
    /// Gets the processes that run as their own executable and that match one of <paramref name="processSpecs"/>.
    /// </summary>
    /// <remarks>
    /// The enumeration is performed on every operating system, and not on Windows alone, because the language
    /// server of the Visual Studio Code C# Dev Kit runs as its own executable on Linux and on macOS as well. The
    /// comparison of the process name is case insensitive, which is what <see cref="Process.GetProcessesByName(string)"/>
    /// performs on every platform.
    /// </remarks>
#pragma warning disable CA1307
    protected IEnumerable<KillableProcess> GetStandaloneProcesses( ImmutableArray<KillableProcessSpec> processSpecs )
        => processSpecs.Where( p => p.IsStandaloneProcess )
            .SelectMany(
                processSpec => SelectOrDispose(
                    Process.GetProcessesByName( processSpec.Name.ToLowerInvariant() ),
                    process => this.SelectStandaloneProcess( process, processSpec ) ) );
#pragma warning restore CA1307

    private KillableProcess? SelectStandaloneProcess( Process process, KillableProcessSpec processSpec )
    {
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
    /// Gets the processes that match one of <paramref name="processSpecs"/>, whether they run as an assembly under
    /// the <c>dotnet</c> process name or as their own executable.
    /// </summary>
    protected IEnumerable<KillableProcess> GetProcesses( ImmutableArray<KillableProcessSpec> processSpecs )
        => this.GetDotNetProcesses( processSpecs ).Concat( this.GetStandaloneProcesses( processSpecs ) );

    public BackstageToolProcessCollection GetToolProcesses()
    {
        var specs = this._toolProcesses.Select( t => t.Spec ).ToImmutableArray();
        var toolProcesses = new List<BackstageToolProcess>();

        try
        {
            foreach ( var process in this.GetProcesses( specs ) )
            {
                // The process passes from the KillableProcess, which is dropped, to the BackstageToolProcess.
                toolProcesses.Add( new BackstageToolProcess( this._toolProcesses.Single( t => t.Spec == process.Spec ).Tool, process.Process ) );
            }
        }
        catch
        {
            foreach ( var toolProcess in toolProcesses )
            {
                toolProcess.Dispose();
            }

            throw;
        }

        return new BackstageToolProcessCollection( toolProcesses );
    }

    public virtual void KillCompilerProcesses( bool shouldEmitWarnings )
    {
        foreach ( var process in this.ExcludeCurrentProcessAndParents( this.GetProcesses( this._processesToKill ), p => p.Process.Id ) )
        {
            using ( process )
            {
                if ( process.Spec.CanShutdownOrKill )
                {
                    process.ShutdownOrKill();
                }
                else if ( shouldEmitWarnings )
                {
                    this.Logger.Warning?.Log(
                        $"The process {process.Process.Id} ({process.Spec.DisplayName ?? process.Spec.Name}), if it uses {this._productProfile.Name}, must be closed manually." );
                }
            }
        }
    }

    /// <summary>
    /// Removes the current process and its parent processes from the processes to stop.
    /// </summary>
    /// <param name="processes">The processes to stop.</param>
    /// <param name="getProcessId">Gets the identifier of a process.</param>
    /// <remarks>
    /// <para>
    /// A process that asks for the clean-up must survive it, and so must the processes that wait for it. The parent of
    /// the current process is often one of the processes to stop: <c>dotnet build</c> and <c>dotnet test</c> run MSBuild
    /// in their own process, and so does a <c>dotnet</c> tool started from a build. Stopping a parent would end the
    /// operation that asked for the clean-up.
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
            this.Logger.Warning?.Log( $"Cannot determine the parent processes, so only the current process is excluded from the clean-up: {e.Message}" );
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