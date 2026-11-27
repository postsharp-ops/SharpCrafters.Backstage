// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Windows;

namespace SharpCrafters.Backstage.Vsx.Windows;

/// <summary>
/// The description of the desktop notifier process of the Visual Studio Tools for PostSharp and Metalama. It is bound to
/// <see cref="VsxProduct.Instance"/>, the product that the extension runs, so that the notifier names the extension in
/// its notifications and acts on the same configuration when the user clicks, snoozes or mutes one.
/// </summary>
internal sealed class VsxDesktopApplicationInfo : BackstageDesktopApplicationInfo
{
    public VsxDesktopApplicationInfo() : base( typeof(VsxDesktopApplicationInfo).Assembly, VsxProduct.Instance ) { }

    /// <inheritdoc />
    public override string Name => "SharpCrafters.Backstage.Vsx.Windows";
}
