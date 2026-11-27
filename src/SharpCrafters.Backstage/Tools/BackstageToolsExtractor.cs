// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Application;
using SharpCrafters.Backstage.Extensibility;
using SharpCrafters.Backstage.Infrastructure;
using SharpCrafters.Backstage.Threading;
using System;
using System.Collections.Immutable;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;

namespace SharpCrafters.Backstage.Tools;

/// <summary>
/// The implementation of <see cref="IBackstageToolsExtractor"/> that extracts the tool applications from the zip
/// archives embedded in one or more assemblies. The archive of a tool is the resource whose name ends with the assembly
/// name of the tool followed by <c>.zip</c>, so the assembly of any product can hold them under its own resource prefix.
/// </summary>
/// <remarks>
/// A product can take its tools from several assemblies, for instance the worker of another product and a desktop
/// notifier of its own. The archive of each tool must be embedded in exactly one of the assemblies. The same assembly
/// can be given more than once, which happens when the assemblies of the tools are merged into one assembly.
/// </remarks>
public sealed class BackstageToolsExtractor : IBackstageToolsExtractor
{
    private readonly IFileSystem _fileSystem;
    private readonly IBackstageToolsLocator _locator;
    private readonly INamedLockService _lockService;
    private readonly ProductProfile _productProfile;
    private readonly ImmutableArray<Assembly> _resourceAssemblies;

    /// <summary>
    /// Initializes a new instance of the <see cref="BackstageToolsExtractor"/> class that takes the archives of the tools
    /// from one assembly.
    /// </summary>
    /// <param name="serviceProvider">The service provider.</param>
    /// <param name="resourceAssembly">The assembly that embeds the zip archives of the tools.</param>
    public BackstageToolsExtractor( IServiceProvider serviceProvider, Assembly resourceAssembly ) : this(
        serviceProvider,
        [resourceAssembly] ) { }

    /// <summary>
    /// Initializes a new instance of the <see cref="BackstageToolsExtractor"/> class that takes the archives of the tools
    /// from several assemblies.
    /// </summary>
    /// <param name="serviceProvider">The service provider.</param>
    /// <param name="resourceAssemblies">The assemblies that embed the zip archives of the tools.</param>
    public BackstageToolsExtractor( IServiceProvider serviceProvider, params Assembly[] resourceAssemblies )
    {
        if ( resourceAssemblies.Length == 0 )
        {
            throw new ArgumentException( "At least one assembly is required.", nameof(resourceAssemblies) );
        }

        this._fileSystem = serviceProvider.GetRequiredBackstageService<IFileSystem>();
        this._locator = serviceProvider.GetRequiredBackstageService<IBackstageToolsLocator>();
        this._lockService = serviceProvider.GetRequiredBackstageService<INamedLockService>();
        this._productProfile = serviceProvider.GetRequiredBackstageService<ProductProfile>();
        this._resourceAssemblies = resourceAssemblies.Distinct().ToImmutableArray();
    }

    private void Extract( BackstageTool tool )
    {
        var directory = this._locator.GetToolDirectory( tool );

        var touchFile = Path.Combine( directory, "unzipped.touch" );

        if ( !this._fileSystem.FileExists( touchFile ) )
        {
            using ( this._lockService.WithGlobalLock( touchFile ) )
            {
                if ( !this._fileSystem.FileExists( touchFile ) )
                {
                    this._fileSystem.CreateDirectory( directory );

                    var (resourceAssembly, zipResourceName) = this.FindArchive( tool );

                    using var resourceStream = resourceAssembly.GetManifestResourceStream( zipResourceName )
                                               ?? throw new InvalidOperationException(
                                                   $"Resource '{zipResourceName}' not found in '{resourceAssembly.Location}'." );

                    using var zipStream = new ZipArchive( resourceStream );
                    this._fileSystem.ExtractZipArchiveToDirectory( zipStream, directory );
                    this._fileSystem.WriteAllText( touchFile, "" );
                }
            }
        }
    }

    private (Assembly Assembly, string ResourceName) FindArchive( BackstageTool tool )
    {
        var zipResourceSuffix = $".{tool.GetAssemblyName( this._productProfile )}.zip";

        var candidates = this._resourceAssemblies
            .SelectMany( assembly => assembly.GetManifestResourceNames()
                             .Where( name => name.EndsWith( zipResourceSuffix, StringComparison.Ordinal ) )
                             .Select( name => (Assembly: assembly, ResourceName: name) ) )
            .ToList();

        return candidates.Count switch
        {
            1 => candidates[0],
            0 => throw new InvalidOperationException(
                $"No resource named '*{zipResourceSuffix}' was found in {this.DescribeAssemblies()}." ),
            _ => throw new InvalidOperationException(
                $"Several resources named '*{zipResourceSuffix}' were found in {this.DescribeAssemblies()}: "
                + string.Join( ", ", candidates.Select( c => $"'{c.ResourceName}'" ) ) + "." )
        };
    }

    private string DescribeAssemblies() => string.Join( ", ", this._resourceAssemblies.Select( a => $"'{a.Location}'" ) );

    public void ExtractAll()
    {
        this.Extract( BackstageTool.Worker );
        this.Extract( BackstageTool.DesktopWindows );
    }
}
