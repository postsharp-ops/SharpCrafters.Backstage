// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using Metalama.Backstage;
using SharpCrafters.Backstage.Extensibility;
using SharpCrafters.Backstage.Testing;
using SharpCrafters.Backstage.Tools;
using SharpCrafters.Backstage.Vsx;
using System;
using System.IO;
using Xunit;

namespace SharpCrafters.Backstage.Tests.Tools;

public sealed class BackstageToolsExtractorTests : TestsBase
{
    public BackstageToolsExtractorTests( ITestOutputHelper logger ) : base( logger )
    {
        // The services take the profile of the extension, which names the notifier differently than the worker.
        this.InitializationOptions = this.InitializationOptions with { Product = MetalamaProduct.Instance with { Profile = VsxProduct.Profile } };
    }

    /// <summary>
    /// Tests that the extractor of the extension takes the worker from the tools of Metalama and the notifier from the tools
    /// of the extension. The tools of Metalama are given twice, as they are when the extension merges the tools assemblies
    /// into one assembly.
    /// </summary>
    [Fact]
    public void TheExtractorTakesEachToolFromTheAssemblyThatEmbedsIt()
    {
        var extractor = new BackstageToolsExtractor(
            this.ServiceProvider,
            typeof(Vsx.Tools.BackstageToolsExtensions).Assembly,
            typeof(Metalama.Backstage.Tools.BackstageToolsExtensions).Assembly,
            typeof(Metalama.Backstage.Tools.BackstageToolsExtensions).Assembly );

        extractor.ExtractAll();

        var locator = this.ServiceProvider.GetRequiredBackstageService<IBackstageToolsLocator>();

        Assert.True(
            this.FileSystem.FileExists( Path.Combine( locator.GetToolDirectory( BackstageTool.Worker ), "Metalama.Backstage.Worker.dll" ) ) );

        Assert.True(
            this.FileSystem.FileExists(
                Path.Combine( locator.GetToolDirectory( BackstageTool.DesktopWindows ), "SharpCrafters.Backstage.Vsx.Windows.exe" ) ) );
    }

    /// <summary>
    /// Tests that the extractor reports a tool whose archive none of its assemblies embeds.
    /// </summary>
    [Fact]
    public void AMissingArchiveIsReported()
    {
        var extractor = new BackstageToolsExtractor( this.ServiceProvider, typeof(Metalama.Backstage.Tools.BackstageToolsExtensions).Assembly );

        var exception = Assert.Throws<InvalidOperationException>( extractor.ExtractAll );
        Assert.Contains( ".SharpCrafters.Backstage.Vsx.Windows.zip", exception.Message, StringComparison.Ordinal );
    }
}
