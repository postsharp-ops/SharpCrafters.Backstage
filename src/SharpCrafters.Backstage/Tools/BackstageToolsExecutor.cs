// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Application;
using SharpCrafters.Backstage.Diagnostics;
using SharpCrafters.Backstage.Extensibility;
using SharpCrafters.Backstage.Infrastructure;
using SharpCrafters.Backstage.Utilities;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace SharpCrafters.Backstage.Tools;

internal sealed class BackstageToolsExecutor : IBackstageToolsExecutor
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IPlatformInfo _platformInfo;
    private readonly ILogger _logger;
    private readonly IProcessExecutor _processExecutor;
    private readonly IBackstageToolsLocator _locator;
    private readonly IFileSystem _fileSystem;
    private readonly ProductProfile _productProfile;

    public BackstageToolsExecutor( IServiceProvider serviceProvider )
    {
        this._serviceProvider = serviceProvider;
        this._platformInfo = serviceProvider.GetRequiredBackstageService<IPlatformInfo>();
        this._logger = serviceProvider.GetLoggerFactory().GetLogger( "BackstageToolExecutor" );
        this._processExecutor = serviceProvider.GetRequiredBackstageService<IProcessExecutor>();
        this._locator = serviceProvider.GetRequiredBackstageService<IBackstageToolsLocator>();
        this._fileSystem = serviceProvider.GetRequiredBackstageService<IFileSystem>();
        this._productProfile = serviceProvider.GetRequiredBackstageService<ProductProfile>();
    }

    public IProcess Start( BackstageTool tool, params string[] arguments )
    {
        if ( this._locator.ToolsMustBeExtracted )
        {
            // This method can be called from different processes, including the Worker processes. These processes do not have 
            // the IBackstageToolsExtractor service, but of course they are guaranteed to run when tools have already been extracted.
            this._serviceProvider.GetBackstageService<IBackstageToolsExtractor>()?.ExtractAll();
        }

        var workerDirectory = this._locator.GetToolDirectory( tool );

        var programPath = Path.Combine( workerDirectory, $"{tool.GetAssemblyName( this._productProfile )}.{(tool.IsExe ? "exe" : "dll")}" );

        if ( !this._fileSystem.FileExists( programPath ) )
        {
            throw new FileNotFoundException( $"The file '{programPath}' does not exist.", programPath );
        }

        ProcessStartInfo processStartInfo;

        if ( tool.IsExe )
        {
            processStartInfo = new ProcessStartInfo()
            {
                FileName = programPath, Arguments = CommandLineArguments.Format( arguments ), UseShellExecute = tool.UseShellExecute, WindowStyle = tool.WindowStyle
            };
        }
        else
        {
            var dotnetPath = this._platformInfo.DotNetExePath;

            // The program path becomes the first argument passed to dotnet.exe.
            var allArguments = new List<string>( arguments.Length + 1 ) { programPath };
            allArguments.AddRange( arguments );

            processStartInfo = new ProcessStartInfo()
            {
                FileName = dotnetPath, Arguments = CommandLineArguments.Format( allArguments ), UseShellExecute = tool.UseShellExecute, WindowStyle = tool.WindowStyle
            };
        }

        this._logger.Info?.Log( $"Starting '{processStartInfo.FileName} {processStartInfo.Arguments}." );

        return this._processExecutor.Start( processStartInfo );
    }
}