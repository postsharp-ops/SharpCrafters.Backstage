// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Extensibility;
using SharpCrafters.Backstage.Infrastructure;
using SharpCrafters.Backstage.Utilities;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace SharpCrafters.Backstage.UserInterface;

internal sealed class LinuxUserInterfaceService( IServiceProvider serviceProvider ) : BrowserBasedUserInterfaceService( serviceProvider )
{
    private const string _xdgOpen = "xdg-open";

    private readonly IEnvironmentVariableProvider _environmentVariableProvider =
        serviceProvider.GetRequiredBackstageService<IEnvironmentVariableProvider>();

    private readonly IFileSystem _fileSystem = serviceProvider.GetRequiredBackstageService<IFileSystem>();

    protected override ProcessStartInfo GetProcessStartInfoForUrl( string url, BrowserMode browserMode )
    {
        // In some scenarios, like building from Visual Studio Code, starting a process with the URL as the file name doesn't
        // work. The URL is therefore opened with xdg-open when it is on the path, which is the case on most Linux
        // distributions.
        var xdgOpenPath = this.FindOnPath( _xdgOpen );

        if ( xdgOpenPath != null )
        {
            // The output is not redirected. xdg-open starts the browser with the same standard output, and nothing reads
            // it, so a redirected browser would block once the pipe is full.
            return new ProcessStartInfo( xdgOpenPath, CommandLineArguments.Format( [url] ) ) { UseShellExecute = false };
        }

        return base.GetProcessStartInfoForUrl( url, browserMode );
    }

    /// <summary>
    /// Finds an executable file on the path, as the shell does: the first existing file with an execute permission.
    /// </summary>
    private string? FindOnPath( string fileName )
        => this._environmentVariableProvider.GetEnvironmentVariable( "PATH" )
            ?.Split( new[] { Path.PathSeparator }, StringSplitOptions.RemoveEmptyEntries )
            .Select( directory => Path.Combine( directory, fileName ) )
            .FirstOrDefault( path => this._fileSystem.FileExists( path ) && IsExecutable( path ) );

    private static bool IsExecutable( string path )
    {
#if NET7_0_OR_GREATER
        try
        {
            // This service is created on Linux only, where the file mode exists.
#pragma warning disable CA1416
            return (File.GetUnixFileMode( path ) & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) != 0;
#pragma warning restore CA1416
        }
        catch ( IOException )
        {
            return false;
        }
        catch ( UnauthorizedAccessException )
        {
            return false;
        }
#else

        // The file mode cannot be read before .NET 7, so an existing file is taken as executable.
        return true;
#endif
    }
}
