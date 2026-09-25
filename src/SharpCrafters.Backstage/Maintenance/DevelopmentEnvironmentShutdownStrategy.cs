// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Application;
using SharpCrafters.Backstage.Extensibility;
using System;
using System.Collections.Immutable;
using System.Diagnostics;

namespace SharpCrafters.Backstage.Maintenance;

/// <summary>
/// Reports the processes of integrated development environments that load the Roslyn analyzers of a product, and leaves
/// them alone.
/// </summary>
/// <remarks>
/// Ending a child process of an integrated development environment leaves that environment in an inconsistent state,
/// and the environment starts the process again as soon as a document is opened, which would lock the files again. The
/// user closes the environment instead. A product whose analyzers these processes load registers this strategy with
/// <see cref="ProcessShutdownServiceExtensions.AddDevelopmentEnvironmentShutdownStrategy"/>.
/// </remarks>
internal sealed class DevelopmentEnvironmentShutdownStrategy : SpecifiedProcessShutdownStrategy
{
    private readonly ProductProfile _productProfile;

    public DevelopmentEnvironmentShutdownStrategy( IServiceProvider serviceProvider ) : base( serviceProvider )
    {
        this._productProfile = serviceProvider.GetRequiredBackstageService<ProductProfile>();
    }

    protected override ImmutableArray<KillableProcessSpec> ProcessSpecs { get; } = ImmutableArray.Create(
        new KillableProcessSpec( "servicehub.roslyncodeanalysisservice", KillableModuleKind.Both, false, false, "Visual Studio" ),

        // Visual Studio 2026 runs the Roslyn analysis process under this name instead. See issue #1463.
        new KillableProcessSpec( "devhub", KillableModuleKind.Both, false, false, "Visual Studio" ),
        new KillableProcessSpec( "jetbrains.resharper.roslyn.worker", KillableModuleKind.DotNet, false, false, "Rider/Resharper" ),
        new KillableProcessSpec( "jetbrains.roslyn.worker", KillableModuleKind.DotNet, false, false, "Rider/Resharper" ),
        new KillableProcessSpec( "omnisharp", KillableModuleKind.DotNet, false, false, "Visual Studio Code / Omnisharp" ),

        // The language server of the Visual Studio Code C# Dev Kit, which runs either as its own executable or as an
        // assembly under 'dotnet'.
        new KillableProcessSpec( "microsoft.codeanalysis.languageserver", KillableModuleKind.Both, false, false, "Visual Studio Code / C# Dev Kit" ) );

    protected override ProcessShutdownResult ShutDownProcess( KillableProcess process, ProcessShutdownOptions options, Stopwatch stopwatch )
        => new(
            process.Spec.DisplayName ?? process.Spec.Name,
            process.Process.Id,
            ProcessShutdownOutcome.Reported,
            $"if it uses {this._productProfile.Name}, close it manually" );
}
