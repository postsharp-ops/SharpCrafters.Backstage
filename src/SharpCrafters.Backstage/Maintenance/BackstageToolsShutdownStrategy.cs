// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Application;
using SharpCrafters.Backstage.Extensibility;
using SharpCrafters.Backstage.Tools;
using System;
using System.Collections.Immutable;
using System.Diagnostics;

namespace SharpCrafters.Backstage.Maintenance;

/// <summary>
/// Ends the tool applications of the product: the worker, whether it uploads the telemetry or hosts the setup web
/// server, and the desktop notifier.
/// </summary>
/// <remarks>
/// The tools are ended without being asked, and with or without <see cref="ProcessShutdownOptions.Force"/>, because they
/// hold no state that ending them can lose: the worker deletes the telemetry files it uploads only once they have been
/// sent, so an upload that is ended is made again the next time. The web server would otherwise keep running for as long
/// as its page is open.
/// </remarks>
internal sealed class BackstageToolsShutdownStrategy : SpecifiedProcessShutdownStrategy
{
    private readonly ProductProfile _productProfile;

    public BackstageToolsShutdownStrategy( IServiceProvider serviceProvider ) : base( serviceProvider )
    {
        this._productProfile = serviceProvider.GetRequiredBackstageService<ProductProfile>();

        this.ProcessSpecs = ImmutableArray.Create(
            // The worker runs under 'dotnet', which hosts the worker assembly, so it is matched as a module.
            new KillableProcessSpec( BackstageTool.Worker.GetAssemblyName( this._productProfile ), KillableModuleKind.DotNet, false ),

            // The notifier is an executable of its own.
            new KillableProcessSpec( BackstageTool.DesktopWindows.GetAssemblyName( this._productProfile ), KillableModuleKind.StandaloneProcess, false ) );
    }

    protected override ImmutableArray<KillableProcessSpec> ProcessSpecs { get; }

    protected override ProcessShutdownResult ShutDownProcess( KillableProcess process, ProcessShutdownOptions options, Stopwatch stopwatch )
    {
        var tool = process.Spec.Name == BackstageTool.Worker.GetAssemblyName( this._productProfile ) ? "worker" : "notifier";

        return Kill( process, $"{this._productProfile.Name} {tool}" );
    }
}
