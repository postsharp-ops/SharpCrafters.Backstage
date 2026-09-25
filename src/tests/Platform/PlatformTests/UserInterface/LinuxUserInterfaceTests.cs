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
    public async Task AWebPageIsOpenedWithXdgOpen()
    {
        const string url = "https://www.postsharp.net/platform-tests";
        var recordPath = Path.Combine( this._directory, "url.txt" );

        // The stub writes the address to a temporary file and renames it, so that the record appears complete.
        var stubPath = Path.Combine( this._directory, "xdg-open" );
        File.WriteAllText( stubPath, $"#!/bin/sh\nprintf '%s' \"$1\" > '{recordPath}.tmp' && mv '{recordPath}.tmp' '{recordPath}'\n" );
        File.SetUnixFileMode( stubPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute );
        Environment.SetEnvironmentVariable( "PATH", $"{this._directory}{Path.PathSeparator}{this._originalPath}" );

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
