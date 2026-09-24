// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Windows;

namespace PostSharp.Backstage.Windows;

/// <summary>
/// The description of the desktop notifier process of PostSharp.
/// </summary>
internal sealed class PostSharpDesktopApplicationInfo : BackstageDesktopApplicationInfo
{
    public PostSharpDesktopApplicationInfo() : base( typeof(PostSharpDesktopApplicationInfo).Assembly, PostSharpProduct.Instance ) { }

    /// <inheritdoc />
    public override string Name => "PostSharp.Backstage.Windows";
}
