// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using System.Runtime.InteropServices;

namespace SharpCrafters.Backstage.PlatformTests;

public static class DotNetHost
{
    /// <summary>
    /// Gets the path of the <c>dotnet</c> executable of the runtime that runs the current process. The runtime directory is
    /// <c>&lt;root&gt;/shared/Microsoft.NETCore.App/&lt;version&gt;/</c>, and the executable is in <c>&lt;root&gt;</c>.
    /// </summary>
    /// <remarks>
    /// It is derived from the runtime rather than read from <c>PATH</c> or <c>DOTNET_ROOT</c>, because the latter are
    /// what the code under test reads.
    /// </remarks>
    public static string ExecutablePath { get; } = Path.Combine(
        Path.GetFullPath( Path.Combine( RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", ".." ) ),
        OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet" );
}
