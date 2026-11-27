// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using Metalama.Backstage;
using PostSharp.Backstage;
using SharpCrafters.Backstage.Tools;
using Xunit;

namespace SharpCrafters.Backstage.Tests.Tools;

public sealed class BackstageToolTests
{
    /// <summary>
    /// Tests that a profile without a prefix for the desktop notifier names both tools with its tool prefix.
    /// </summary>
    [Fact]
    public void WithoutADesktopPrefixBothToolsUseTheToolPrefix()
    {
        Assert.Equal( "Metalama.Backstage.Worker", BackstageTool.Worker.GetAssemblyName( MetalamaProduct.Profile ) );
        Assert.Equal( "Metalama.Backstage.Windows", BackstageTool.DesktopWindows.GetAssemblyName( MetalamaProduct.Profile ) );
        Assert.Equal( "PostSharp.Backstage.Worker", BackstageTool.Worker.GetAssemblyName( PostSharpProduct.Profile ) );
        Assert.Equal( "PostSharp.Backstage.Windows", BackstageTool.DesktopWindows.GetAssemblyName( PostSharpProduct.Profile ) );
    }

    /// <summary>
    /// Tests that the prefix for the desktop notifier renames the notifier only.
    /// </summary>
    [Fact]
    public void TheDesktopPrefixRenamesTheNotifierOnly()
    {
        var profile = MetalamaProduct.Profile with { DesktopToolAssemblyNamePrefix = "Other" };

        Assert.Equal( "Metalama.Backstage.Worker", BackstageTool.Worker.GetAssemblyName( profile ) );
        Assert.Equal( "Other.Windows", BackstageTool.DesktopWindows.GetAssemblyName( profile ) );
    }
}
