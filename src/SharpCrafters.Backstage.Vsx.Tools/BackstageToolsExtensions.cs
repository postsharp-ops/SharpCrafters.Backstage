// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using JetBrains.Annotations;
using SharpCrafters.Backstage.Extensibility;
using SharpCrafters.Backstage.Tools;

namespace SharpCrafters.Backstage.Vsx.Tools;

/// <summary>
/// Registers the extractor of the tool applications of the Visual Studio Tools for PostSharp and Metalama: the desktop
/// notifier of the extension, which this assembly embeds as a zip archive, and the worker of Metalama, which
/// <c>Metalama.Backstage.Tools</c> embeds.
/// </summary>
[PublicAPI]
public static class BackstageToolsExtensions
{
    public static ServiceProviderBuilder AddTools( this ServiceProviderBuilder builder )
    {
        builder.AddService(
            typeof(IBackstageToolsExtractor),
            serviceProvider => new BackstageToolsExtractor(
                serviceProvider,
                typeof(BackstageToolsExtensions).Assembly,
                typeof(Metalama.Backstage.Tools.BackstageToolsExtensions).Assembly ) );

        return builder;
    }
}
