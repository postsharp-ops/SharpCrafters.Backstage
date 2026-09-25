// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Extensibility;
using SharpCrafters.Backstage.Testing;
using SharpCrafters.Backstage.UserInterface;
using System.Runtime.Versioning;
using Xunit;

namespace SharpCrafters.Backstage.PlatformTests.UserInterface;

/// <summary>
/// Runs the user interface service of Linux, which opens a web page with <c>xdg-open</c> when it is on the path. A stub
/// <c>xdg-open</c> records the address instead of starting a browser.
/// </summary>
public sealed class LinuxUserInterfaceTests : IDisposable
{
    private readonly string _directory = Path.Combine( Path.GetTempPath(), "PlatformTests", Guid.NewGuid().ToString( "N" ) );

    private readonly string? _originalPath = Environment.GetEnvironmentVariable( "PATH" );

    public LinuxUserInterfaceTests()
    {
        Directory.CreateDirectory( this._directory );
    }

    [PlatformFact( TestPlatforms.Linux )]
    [UnsupportedOSPlatform( "windows" )]
    public Task AWebPageIsOpenedWithXdgOpen() => this.OpenWebPageAsync( withNonExecutableShadow: false );

    /// <summary>
    /// The shell skips a file of the path that has no execute permission, and so must the service.
    /// </summary>
    [PlatformFact( TestPlatforms.Linux )]
    [UnsupportedOSPlatform( "windows" )]
    public Task ANonExecutableXdgOpenEarlierOnThePathIsSkipped() => this.OpenWebPageAsync( withNonExecutableShadow: true );

    [UnsupportedOSPlatform( "windows" )]
    private async Task OpenWebPageAsync( bool withNonExecutableShadow )
    {
        const string url = "https://www.postsharp.net/platform-tests";
        var recordPath = Path.Combine( this._directory, "url.txt" );
        var stubDirectory = Directory.CreateDirectory( Path.Combine( this._directory, "stub" ) ).FullName;
        var path = stubDirectory;

        // The stub writes the address to a temporary file and renames it, so that the record appears complete.
        var stubPath = Path.Combine( stubDirectory, "xdg-open" );
        File.WriteAllText( stubPath, $"#!/bin/sh\nprintf '%s' \"$1\" > '{recordPath}.tmp' && mv '{recordPath}.tmp' '{recordPath}'\n" );
        File.SetUnixFileMode( stubPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute );

        if ( withNonExecutableShadow )
        {
            var shadowDirectory = Directory.CreateDirectory( Path.Combine( this._directory, "shadow" ) ).FullName;
            var shadowPath = Path.Combine( shadowDirectory, "xdg-open" );
            File.WriteAllText( shadowPath, "#!/bin/sh\nexit 1\n" );
            File.SetUnixFileMode( shadowPath, UnixFileMode.UserRead | UnixFileMode.UserWrite );
            path = $"{shadowDirectory}{Path.PathSeparator}{path}";
        }

        Environment.SetEnvironmentVariable( "PATH", $"{path}{Path.PathSeparator}{this._originalPath}" );

        var recorded = new TaskCompletionSource( TaskCreationOptions.RunContinuationsAsynchronously );
        using var watcher = new FileSystemWatcher( this._directory, Path.GetFileName( recordPath ) ) { EnableRaisingEvents = true };
        watcher.Renamed += ( _, _ ) => recorded.TrySetResult();

        var userInterfaceService = PlatformTestServices.CreateServiceProvider( addUserInterface: true ).GetRequiredBackstageService<IUserInterfaceService>();
        userInterfaceService.OpenExternalWebPage( url, BrowserMode.Default );

        if ( !File.Exists( recordPath ) )
        {
            await recorded.Task.WaitAsync( HelperProcess.Timeout );
        }

        Assert.Equal( url, File.ReadAllText( recordPath ) );
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable( "PATH", this._originalPath );
        Directory.Delete( this._directory, recursive: true );
    }
}
