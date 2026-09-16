// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Application;
using SharpCrafters.Backstage.Extensibility;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SharpCrafters.Backstage.Tools;

/// <summary>
/// The implementation of <see cref="IBackstageToolsLocator"/> of a development environment, which finds the worker in
/// the build output of the current repository instead of extracting it.
/// </summary>
internal sealed class DevBackstageToolsLocator : IBackstageToolsLocator
{
#if DEBUG
    private const string _buildConfiguration = "Debug";
#else
    private const string _buildConfiguration = "Release";
#endif

    /// <summary>
    /// The name of the repository that builds the tools. A product repository that runs from source is checked out beside
    /// it, under the same parent directory.
    /// </summary>
    private const string _backstageRepositoryName = "SharpCrafters.Backstage";

    private static readonly string _rootDirectory = FindRootDirectory();

    private readonly ProductProfile _productProfile;

    public DevBackstageToolsLocator( IServiceProvider serviceProvider )
    {
        this._productProfile = serviceProvider.GetRequiredBackstageService<ProductProfile>();
    }

    /// <summary>
    /// Finds the root of the repository from which the current program runs. The <c>.git</c> entry is a directory in a
    /// primary checkout and a file in a git worktree.
    /// </summary>
    private static string FindRootDirectory()
    {
        for ( var directory = Path.GetDirectoryName( Environment.GetCommandLineArgs()[0] ); directory != null; directory = Path.GetDirectoryName( directory ) )
        {
            var gitEntry = Path.Combine( directory, ".git" );

            if ( Directory.Exists( gitEntry ) || File.Exists( gitEntry ) )
            {
                return directory;
            }
        }

        throw new FileNotFoundException( "Cannot find the repo directory." );
    }

    /// <summary>
    /// Gets the build output directories in which a tool may be found, in the order in which they are tried: first the
    /// repository from which the program runs, for the case where that repository is the one that builds the tools, then
    /// the checkout of that repository beside it, for the case where a product repository runs from source.
    /// </summary>
    private static IEnumerable<string> GetCandidateToolDirectories( string assemblyName, string outputSubdirectory )
    {
        var parentDirectory = Path.GetDirectoryName( _rootDirectory );

        string[] repositoryDirectories = parentDirectory == null
            ? [_rootDirectory]
            : [_rootDirectory, Path.Combine( parentDirectory, _backstageRepositoryName )];

        foreach ( var repositoryDirectory in repositoryDirectories )
        {
            yield return Path.Combine( repositoryDirectory, "src", assemblyName, "bin", _buildConfiguration, "net10.0", outputSubdirectory );
        }
    }

    public bool ToolsMustBeExtracted => false;

    public string GetToolDirectory( BackstageTool tool )
    {
        if ( tool != BackstageTool.Worker )
        {
            throw new ArgumentOutOfRangeException( nameof(tool), $"The tool '{tool}' is not available in the development environment." );
        }

        // The published output of the worker project, which contains the static web assets.
        var candidates = GetCandidateToolDirectories( tool.GetAssemblyName( this._productProfile ), "Packed" ).ToList();

        return candidates.FirstOrDefault( Directory.Exists )
               ?? throw new DirectoryNotFoundException(
                   $"The tool '{tool}' has not been built. None of these directories exists: {string.Join( ", ", candidates )}." );
    }
}
