// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Worker.Worker;

namespace SharpCrafters.Backstage.Vsx.Worker;

/// <summary>
/// The description of the worker process of the Visual Studio Tools for PostSharp and Metalama. It is bound to
/// <see cref="VsxProduct.Instance"/>, the product that the extension runs, so that the worker uploads the telemetry of the
/// extension and names the extension in its web pages.
/// </summary>
internal sealed class VsxWorkerApplicationInfo : BackstageWorkerApplicationInfo
{
    public VsxWorkerApplicationInfo() : base( typeof(VsxWorkerApplicationInfo).Assembly, VsxProduct.Instance ) { }

    /// <inheritdoc />
    public override string Name => "Visual Studio Tools for PostSharp and Metalama Backstage Worker";
}
