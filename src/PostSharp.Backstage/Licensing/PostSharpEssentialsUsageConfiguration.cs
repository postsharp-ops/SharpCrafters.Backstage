// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using JetBrains.Annotations;
using SharpCrafters.Backstage.Configuration;
using System.Collections.Immutable;

namespace PostSharp.Backstage;

/// <summary>
/// How many lines of code each assembly has had enhanced under the free edition, which is what the cap of that
/// edition is applied to across the projects of a solution.
/// </summary>
/// <remarks>
/// <para>
/// The free edition caps the lines of code of one project and the lines of code of the solution it is part of. A
/// compilation sees one project, so the second number cannot be counted within it: each compilation records what it
/// enhanced, and reads the records of the assemblies the project references. The count of an assembly is therefore
/// the count of that assembly plus the counts of everything it references, which is what makes the number a
/// compilation reads a total for the closure below it rather than for one assembly.
/// </para>
/// <para>
/// This is a file of this version and not a registry key shared with PostSharp 2026.0, unlike the registered license
/// keys and the license audit record. The two versions count differently, so a shared record would mean each
/// correcting the other. A solution built with both versions is counted once by each.
/// </para>
/// </remarks>
[ConfigurationFile( "essentialsUsage.json" )]
[PublicAPI]
public sealed record PostSharpEssentialsUsageConfiguration : ConfigurationFile
{
    /// <summary>
    /// Gets the number of lines of code enhanced under the free edition by each assembly and the assemblies it
    /// references, keyed by the full name of the assembly.
    /// </summary>
    /// <remarks>
    /// The full name and not the simple name, because two assemblies of the same simple name and different versions
    /// are two assemblies here, as they are to the compiler that reads the references of a project.
    /// </remarks>
    public ImmutableDictionary<string, int> LinesOfCodeByAssembly { get; init; } = ImmutableDictionary<string, int>.Empty;

    /// <summary>
    /// Gets the number of lines of code recorded for an assembly and the assemblies it references, or zero when
    /// nothing is recorded for it.
    /// </summary>
    /// <param name="assemblyFullName">The full name of the assembly.</param>
    public int GetLinesOfCode( string assemblyFullName )
        => this.LinesOfCodeByAssembly.TryGetValue( assemblyFullName, out var linesOfCode ) ? linesOfCode : 0;

    /// <summary>
    /// Records the number of lines of code that an assembly and the assemblies it references have had enhanced.
    /// </summary>
    /// <param name="assemblyFullName">The full name of the assembly.</param>
    /// <param name="linesOfCode">The number of lines of code. Zero removes the record of the assembly.</param>
    /// <remarks>
    /// Zero removes the record instead of storing it, so that an assembly that stops using the free edition stops
    /// counting towards the cap of the solution, and the file does not keep a row per assembly ever built.
    /// </remarks>
    public PostSharpEssentialsUsageConfiguration SetLinesOfCode( string assemblyFullName, int linesOfCode )
        => this with
        {
            LinesOfCodeByAssembly = linesOfCode == 0
                ? this.LinesOfCodeByAssembly.Remove( assemblyFullName )
                : this.LinesOfCodeByAssembly.SetItem( assemblyFullName, linesOfCode )
        };
}
