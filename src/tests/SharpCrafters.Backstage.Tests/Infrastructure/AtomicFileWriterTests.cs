// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Utilities;
using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Xunit;

namespace SharpCrafters.Backstage.Tests.Infrastructure;

/// <summary>
/// Tests <see cref="AtomicFileWriter"/> against the real file system, in the conditions under which the substitution of
/// the destination fails on Windows. See https://github.com/metalama/Metalama/issues/2317.
/// </summary>
public sealed class AtomicFileWriterTests : IDisposable
{
    private const string _previousContent = "previous";
    private const string _newContent = "new content";

    private readonly ITestOutputHelper _logger;
    private readonly string _directory;

    public AtomicFileWriterTests( ITestOutputHelper logger )
    {
        this._logger = logger;
        this._directory = Path.Combine( Path.GetTempPath(), "Metalama.AtomicFileWriterTests", Guid.NewGuid().ToString( "N" ) );
        Directory.CreateDirectory( this._directory );
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete( this._directory, recursive: true );
        }
        catch ( Exception e )
        {
            this._logger.WriteLine( $"Could not delete '{this._directory}': {e.Message}" );
        }
    }

    private string GetPath() => Path.Combine( this._directory, "file.txt" );

    private string[] GetFileNames() => Directory.GetFiles( this._directory ).Select( Path.GetFileName ).ToArray()!;

    [Fact]
    public void ReplacesTheContentOfAnExistingFile()
    {
        var path = this.GetPath();
        File.WriteAllText( path, _previousContent );

        Assert.True( AtomicFileWriter.TryWriteAllText( path, _newContent, AtomicFileWriterFallbacks.None, null, out var exception ) );
        Assert.Null( exception );
        Assert.Equal( _newContent, File.ReadAllText( path ) );
        Assert.Equal( new[] { Path.GetFileName( path ) }, this.GetFileNames() );
    }

    /// <summary>
    /// A reader that shares writing but not deletion prevents the destination from being replaced or renamed over, but
    /// not from being written in place. This is how the touch file of metalama/Metalama#2317 was held.
    /// </summary>
    [Fact]
    public void WritesInPlaceWhenTheDestinationCannotBeSubstituted()
    {
        Assert.SkipUnless( RuntimeInformation.IsOSPlatform( OSPlatform.Windows ), "On Unix an open descriptor does not prevent the substitution." );

        var path = this.GetPath();
        File.WriteAllText( path, _previousContent );

        using ( new FileStream( path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite ) )
        {
            Assert.True( AtomicFileWriter.TryWriteAllText( path, _newContent, AtomicFileWriterFallbacks.WriteInPlace, null, out var exception ) );
            Assert.Null( exception );
        }

        Assert.Equal( _newContent, File.ReadAllText( path ) );
        Assert.Equal( new[] { Path.GetFileName( path ) }, this.GetFileNames() );
    }

    /// <summary>
    /// The rename fallback is atomic, and a reader that shares writing but not deletion prevents it as much as it prevents
    /// the replacement, so allowing it alone does not make the write succeed.
    /// </summary>
    [Fact]
    public void RenameOverwriteAloneDoesNotWriteInPlace()
    {
        Assert.SkipUnless( RuntimeInformation.IsOSPlatform( OSPlatform.Windows ), "On Unix an open descriptor does not prevent the substitution." );

        var path = this.GetPath();
        File.WriteAllText( path, _previousContent );

        using ( new FileStream( path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite ) )
        {
            Assert.False( AtomicFileWriter.TryWriteAllText( path, _newContent, AtomicFileWriterFallbacks.RenameOverwrite, null, out _ ) );
        }

        Assert.Equal( _previousContent, File.ReadAllText( path ) );
        Assert.Equal( new[] { Path.GetFileName( path ) }, this.GetFileNames() );
    }

    [Fact]
    public void DoesNotWriteInPlaceUnlessAllowed()
    {
        Assert.SkipUnless( RuntimeInformation.IsOSPlatform( OSPlatform.Windows ), "On Unix an open descriptor does not prevent the substitution." );

        var path = this.GetPath();
        File.WriteAllText( path, _previousContent );

        using ( new FileStream( path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite ) )
        {
            Assert.False( AtomicFileWriter.TryWriteAllText( path, _newContent, AtomicFileWriterFallbacks.None, null, out var exception ) );
            Assert.NotNull( exception );
        }

        Assert.Equal( _previousContent, File.ReadAllText( path ) );
        Assert.Equal( new[] { Path.GetFileName( path ) }, this.GetFileNames() );
    }

    /// <summary>
    /// A reader that shares neither writing nor deletion defeats every strategy. The method must then return
    /// <see langword="false"/> instead of throwing, and leave no temporary file behind.
    /// </summary>
    [Fact]
    public void ReturnsFalseWithoutThrowingWhenEveryStrategyFails()
    {
        Assert.SkipUnless( RuntimeInformation.IsOSPlatform( OSPlatform.Windows ), "On Unix an open descriptor does not prevent the substitution." );

        var path = this.GetPath();
        File.WriteAllText( path, _previousContent );

        using ( new FileStream( path, FileMode.Open, FileAccess.Read, FileShare.Read ) )
        {
            Assert.False( AtomicFileWriter.TryWriteAllText( path, _newContent, AtomicFileWriterFallbacks.All, null, out var exception ) );
            Assert.True( exception is IOException or UnauthorizedAccessException, exception?.ToString() );
        }

        Assert.Equal( _previousContent, File.ReadAllText( path ) );
        Assert.Equal( new[] { Path.GetFileName( path ) }, this.GetFileNames() );
    }
}
