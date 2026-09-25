// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Application;
using SharpCrafters.Backstage.Diagnostics;
using SharpCrafters.Backstage.Extensibility;
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

    private const string _dotNetProcessName = "dotnet";

    protected ILogger Logger { get; }

    /// <summary>
    /// The profile of the product, which gives the prefix of the assemblies that identify a process as using the
    /// product.
    /// </summary>
    private readonly ProductProfile _productProfile;

    protected ProcessManagerBase( IServiceProvider serviceProvider )
    {
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
    protected List<Process> GetCandidateProcesses( ImmutableArray<KillableProcessSpec> processSpecs )
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
    /// The <see cref="KillableProcess"/> objects do not own their processes: the caller of <see cref="GetCandidateProcesses"/>
    /// disposes them.
    /// </remarks>
    protected IEnumerable<KillableProcess> GetKillableProcesses( IEnumerable<Process> candidates, ImmutableArray<KillableProcessSpec> processSpecs )
    {
        foreach ( var process in candidates )
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

    private static void Dispose( List<Process> processes )
    {
        foreach ( var process in processes )
        {
            process.Dispose();
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

    public IReadOnlyList<ToolProcessShutdownResult> ShutDownToolProcesses()
    {
        var specs = this._toolProcesses.Select( t => t.Spec ).ToImmutableArray();
        var results = new List<ToolProcessShutdownResult>();
        var candidates = this.GetCandidateProcesses( specs );

        try
        {
            using var currentProcess = Process.GetCurrentProcess();

            foreach ( var process in this.GetKillableProcesses( candidates, specs ) )
            {
                if ( process.Process.Id == currentProcess.Id )
                {
                    continue;
                }

                var tool = this._toolProcesses.Single( t => t.Spec == process.Spec ).Tool;
                var hasExited = process.ShutdownOrKill( out var errorMessage );

                results.Add( new ToolProcessShutdownResult( tool, process.Process.Id, hasExited, errorMessage ) );
            }
        }
        finally
        {
            Dispose( candidates );
        }

        return results;
    }

    public virtual void KillCompilerProcesses( bool shouldEmitWarnings )
    {
        var candidates = this.GetCandidateProcesses( this._processesToKill );

        try
        {
            foreach ( var process in this.GetKillableProcesses( candidates, this._processesToKill ) )
            {
                if ( process.Spec.CanShutdownOrKill )
                {
                    // Failures are logged by ShutdownOrKill.
                    process.ShutdownOrKill( out _ );
                }
                else if ( shouldEmitWarnings )
                {
                    this.Logger.Warning?.Log(
                        $"The process {process.Process.Id} ({process.Spec.DisplayName ?? process.Spec.Name}), if it uses {this._productProfile.Name}, must be closed manually." );
                }
            }
        }
        finally
        {
            Dispose( candidates );
        }
    }
}