// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Application;

namespace PostSharp.Backstage.TestTool;

internal sealed class ApplicationInfo : ApplicationInfoBase
{
    public ApplicationInfo() : base( typeof(ApplicationInfo).Assembly, PostSharpProduct.Profile ) { }

    public override string Name => typeof(ApplicationInfo).Assembly.GetName().Name!;
}
