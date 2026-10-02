// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

namespace SharpCrafters.Backstage.Maintenance;

/// <summary>
/// Describes a process to find: the name of its executable, or of the assembly that <c>dotnet</c> runs, depending on
/// <paramref name="Kind"/>.
/// </summary>
/// <param name="DisplayName">The name under which the process is reported, or <c>null</c> to report <paramref name="Name"/>.</param>
internal readonly record struct ProcessSpec( string Name, ProcessModuleKind Kind, string? DisplayName = null )
{
    public bool IsDotNet => (this.Kind & ProcessModuleKind.DotNet) != 0;

    public bool IsStandaloneProcess => (this.Kind & ProcessModuleKind.StandaloneProcess) != 0;
}
