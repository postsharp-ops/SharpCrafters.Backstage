// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Worker;
using System.Threading.Tasks;

namespace PostSharp.Backstage;

/// <summary>
/// The entry point of the worker of PostSharp, which binds the worker library to the PostSharp product.
/// </summary>
internal static class Program
{
    public static Task<int> Main( string[] args ) => BackstageWorkerProgram.RunAsync( args, new PostSharpWorkerApplicationInfo() );
}
