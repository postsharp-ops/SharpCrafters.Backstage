// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Application;
using SharpCrafters.Backstage.Extensibility;
using System;
using System.Collections.Immutable;
using System.Collections.Generic;
using System.Linq;

namespace SharpCrafters.Backstage.Maintenance;

/// <summary>
/// Stops the processes of integrated development environments that load the Roslyn analyzers of a product: reports them,
/// and ends them only when <see cref="ProcessShutdownOptions.All"/> is set.
/// </summary>
/// <remarks>
/// Ending a child process of an integrated development environment leaves that environment in an inconsistent state,
/// and the environment starts the process again as soon as a document is opened, which would lock the files again. The
/// user therefore closes the environment instead, unless they ask for these processes with <c>--all</c>. They have no
/// shutdown request, so they are ended. A product whose analyzers these processes load registers this strategy with
/// <see cref="ProcessShutdownServiceExtensions.AddDevelopmentEnvironmentShutdownStrategy"/>.
/// </remarks>
internal sealed class DevelopmentEnvironmentShutdownStrategy : SpecifiedProcessShutdownStrategy
{
    private readonly ProductProfile _productProfile;

    public DevelopmentEnvironmentShutdownStrategy( IServiceProvider serviceProvider ) : base( serviceProvider )
    {
        this._productProfile = serviceProvider.GetRequiredBackstageService<ProductProfile>();
    }

    protected override ImmutableArray<ProcessSpec> ProcessSpecs { get; } = ImmutableArray.Create(
        new ProcessSpec( "servicehub.roslyncodeanalysisservice", ProcessModuleKind.Both, "Visual Studio" ),

        // Visual Studio 2026 runs the Roslyn analysis process under this name instead. See issue #1463.
        new ProcessSpec( "devhub", ProcessModuleKind.Both, "Visual Studio" ),
        new ProcessSpec( "jetbrains.resharper.roslyn.worker", ProcessModuleKind.DotNet, "Rider/Resharper" ),
        new ProcessSpec( "jetbrains.roslyn.worker", ProcessModuleKind.DotNet, "Rider/Resharper" ),
        new ProcessSpec( "omnisharp", ProcessModuleKind.DotNet, "Visual Studio Code / Omnisharp" ),

        // The language server of the Visual Studio Code C# Dev Kit, which runs either as its own executable or as an
        // assembly under 'dotnet'.
        new ProcessSpec( "microsoft.codeanalysis.languageserver", ProcessModuleKind.Both, "Visual Studio Code / C# Dev Kit" ) );

    protected override IReadOnlyList<ProcessShutdownResult> ShutDown( IReadOnlyList<MatchedProcess> processes, ProcessShutdownOptions options )
        => processes.Select(
                process =>
                {
                    var description = process.Spec.DisplayName ?? process.Spec.Name;

                    return options.All
                        ? this.Kill( process, description )
                        : new ProcessShutdownResult(
                            description,
                            process.Process.Id,
                            ProcessShutdownOutcome.Reported,
                            $"if it uses {this._productProfile.Name}, close it, or use --all to end it" );
                } )
            .ToList();
}
