// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using Microsoft.VisualStudio.SolutionPersistence.Model;
using Microsoft.VisualStudio.SolutionPersistence.Serializer;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace SharpCrafters.Aspects.Introspection.Query.Projects;

/// <summary>
/// Reads the projects of a <c>.sln</c> or <c>.slnx</c> file.
/// </summary>
internal static class SolutionReader
{
    /// <summary>
    /// Gets the full paths of the project files of a solution, in the order of the solution. Solution folders and project files
    /// that do not exist are left out.
    /// </summary>
    public static IReadOnlyList<string> GetProjects( string solutionPath )
    {
        var serializer = SolutionSerializers.GetSerializerByMoniker( solutionPath )
                         ?? throw new IntrospectionCommandException( $"'{solutionPath}' is not a .sln or .slnx file.", IntrospectionExitCodes.Error );

        SolutionModel solution;

        try
        {
            solution = serializer.OpenAsync( solutionPath, CancellationToken.None ).GetAwaiter().GetResult();
        }
        catch ( Exception e ) when ( e is SolutionException or IOException or FormatException )
        {
            throw new IntrospectionCommandException( $"The solution '{solutionPath}' cannot be read: {e.Message}", IntrospectionExitCodes.Error );
        }

        var directory = Path.GetDirectoryName( Path.GetFullPath( solutionPath ) )!;

        return solution.SolutionProjects
            .Select( p => Path.GetFullPath( Path.Combine( directory, p.FilePath.Replace( '\\', Path.DirectorySeparatorChar ) ) ) )
            .Where( File.Exists )
            .ToList();
    }
}
