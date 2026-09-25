// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Extensibility;
using SharpCrafters.Backstage.Infrastructure;
using SharpCrafters.Backstage.PlatformTests.Conditions;
using SharpCrafters.Backstage.UserInterface;
using System.Runtime.Versioning;
using Xunit;

namespace SharpCrafters.Backstage.PlatformTests.FileSystem;

/// <summary>
/// Runs the file operations whose result depends on the file system of the operating system. The unit tests run them
/// against a Windows file system only.
/// </summary>
public sealed class FileSystemTests : IDisposable
{
    private readonly string _directory = Path.Combine( Path.GetTempPath(), "PlatformTests", Guid.NewGuid().ToString( "N" ) );

    public FileSystemTests()
    {
        Directory.CreateDirectory( this._directory );
    }

    /// <summary>
    /// The token authenticates the requests to the local setup web server, so no other user may read it. Unix has no
    /// per-user temporary directory, so the file mode is what restricts it.
    /// </summary>
    [PlatformFact( TestPlatforms.Unix )]
    [UnsupportedOSPlatform( "windows" )]
    public void TheTokenFileIsReadableAndWritableByItsOwnerOnly()
    {
        var path = SetupWebServerToken.WriteTokenFile( this._directory, "token" );

        Assert.Equal( UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode( path ) );
    }

    /// <summary>
    /// On Windows, the substitution fails while a reader holds the destination open, and the unit tests cover the retry
    /// that absorbs it. On Unix, the substitution is a rename, which succeeds while the destination is open.
    /// </summary>
    [PlatformFact( TestPlatforms.Unix )]
    public void AnAtomicWriteReplacesAFileThatIsOpenForReading()
    {
        var fileSystem = PlatformTestServices.CreateServiceProvider().GetRequiredBackstageService<IFileSystem>();
        var path = Path.Combine( this._directory, "file.txt" );
        File.WriteAllText( path, "old" );

        using ( new FileStream( path, FileMode.Open, FileAccess.Read, FileShare.Read ) )
        {
            fileSystem.WriteAllTextAtomically( path, "new" );
        }

        Assert.Equal( "new", File.ReadAllText( path ) );
    }

    public void Dispose() => Directory.Delete( this._directory, recursive: true );
}
