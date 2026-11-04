// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

namespace SharpCrafters.Aspects.Introspection.Query.Projects;

/// <summary>
/// A .NET SDK whose MSBuild the process loads.
/// </summary>
/// <param name="Version">The version of the SDK, as <c>dotnet --list-sdks</c> gives it, or an empty string when it is unknown.</param>
/// <param name="Directory">The directory of the SDK, which holds its MSBuild, or an empty string when it is unknown.</param>
public sealed record DotNetSdk( string Version, string Directory );
