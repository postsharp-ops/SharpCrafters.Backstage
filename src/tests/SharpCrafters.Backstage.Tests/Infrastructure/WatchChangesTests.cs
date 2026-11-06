// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Infrastructure;
using SharpCrafters.Backstage.Testing;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace SharpCrafters.Backstage.Tests.Infrastructure;

/// <summary>
/// Tests the contract of <see cref="IFileSystem.WatchChanges"/>.
/// </summary>
/// <remarks>
/// The same tests run against the real file system and against <see cref="TestFileSystem"/>, which the products
/// consuming this package use in their own tests. A test of theirs that passes with the double must also pass with
/// the implementation it stands for, so the double must not accept what the real watcher refuses nor miss an event
/// the real watcher raises.
/// </remarks>
public abstract class WatchChangesTests : TestsBase, IDisposable
{
    /// <summary>
    /// Bounds every wait for an event, so that a missing event surfaces as a failed assertion rather than as a hung
    /// test run. It is a guard and never a synchronization mechanism: no test depends on its duration.
    /// </summary>
    private readonly CancellationTokenSource _timeout = new( TimeSpan.FromSeconds( 30 ) );

    protected WatchChangesTests( ITestOutputHelper logger ) : base( logger ) { }

    protected abstract IFileSystem WatchedFileSystem { get; }

    /// <summary>
    /// Gets an existing directory, empty and private to the test.
    /// </summary>
    protected abstract string Directory { get; }

    /// <summary>
    /// Tests that watching a directory that does not exist fails. A product that watches the output directory of a
    /// project before the first build has created it must handle the failure; if the test double accepted the call,
    /// its tests would pass while the product failed on the machine of the customer.
    /// </summary>
    [Fact]
    public void MissingDirectoryThrowsArgumentException()
    {
        var missingDirectory = Path.Combine( this.Directory, "missing" );

        Assert.ThrowsAny<ArgumentException>( () => this.WatchedFileSystem.WatchChanges( missingDirectory, "*.json", _ => { } ) );
    }

    /// <summary>
    /// Tests that a file renamed into the filter is reported. A tool that writes a temporary file and renames it
    /// over the final name, which is how a file is written atomically, would otherwise never be noticed, and the
    /// watcher would keep serving the previous content.
    /// </summary>
    [Fact]
    public async Task RenameMatchingNewNameRaisesRenamed()
    {
        var oldPath = Path.Combine( this.Directory, "file.tmp" );
        var newPath = Path.Combine( this.Directory, "file.json" );
        this.WatchedFileSystem.WriteAllText( oldPath, "content" );

        var renamed = await this.WatchUntilRenamedAsync( () => this.WatchedFileSystem.MoveFile( oldPath, newPath ) );

        Assert.Equal( WatcherChangeTypes.Renamed, renamed.ChangeType );
        Assert.Equal( "file.tmp", renamed.OldName );
        Assert.Equal( "file.json", renamed.Name );
        Assert.Equal( newPath, renamed.FullPath, ignoreCase: true );
        Assert.Equal( oldPath, renamed.OldFullPath, ignoreCase: true );
    }

    /// <summary>
    /// Tests that a file renamed out of the filter is reported. Without it, a watcher would keep a file it believes
    /// present after it has gone.
    /// </summary>
    [Fact]
    public async Task RenameMatchingOldNameRaisesRenamed()
    {
        var oldPath = Path.Combine( this.Directory, "file.json" );
        var newPath = Path.Combine( this.Directory, "file.bak" );
        this.WatchedFileSystem.WriteAllText( oldPath, "content" );

        var renamed = await this.WatchUntilRenamedAsync( () => this.WatchedFileSystem.MoveFile( oldPath, newPath ) );

        Assert.Equal( "file.json", renamed.OldName );
        Assert.Equal( "file.bak", renamed.Name );
    }

    /// <summary>
    /// Tests that creating several missing levels of directories at once notifies a watcher of the outermost existing
    /// one, with the level created in it. A product waiting for an output directory that the first build will create
    /// watches its nearest existing ancestor and re-arms on each level that appears; if the creation were reported
    /// only for the innermost directory, in a directory nobody could watch yet, it would never notice the build.
    /// </summary>
    [Fact]
    public async Task CreatingNestedDirectoriesRaisesCreatedForOutermostLevel()
    {
        var createdNames = await this.WatchCreatedUntilMarkerAsync(
            () => this.WatchedFileSystem.CreateDirectory( Path.Combine( this.Directory, "b", "c" ) ) );

        Assert.Equal( ["b"], createdNames );
    }

    /// <summary>
    /// Tests that creating a directory that already exists is not reported. A watcher re-arming on a creation would
    /// otherwise react to a build that changed nothing.
    /// </summary>
    [Fact]
    public async Task CreatingExistingDirectoryRaisesNothing()
    {
        var existing = Path.Combine( this.Directory, "existing" );
        this.WatchedFileSystem.CreateDirectory( existing );

        var createdNames = await this.WatchCreatedUntilMarkerAsync( () => this.WatchedFileSystem.CreateDirectory( existing ) );

        Assert.Empty( createdNames );
    }

    /// <summary>
    /// Watches <see cref="Directory"/> for every name, runs an action, then writes a marker file, and returns the
    /// names of the <see cref="WatcherChangeTypes.Created"/> events received other than the marker's, once the
    /// marker's has been received.
    /// </summary>
    /// <remarks>
    /// The marker bounds the observation without a delay. Events of other change types are ignored, because the real
    /// file system also reports a <see cref="WatcherChangeTypes.Changed"/> of a directory when its content changes.
    /// </remarks>
    private async Task<string[]> WatchCreatedUntilMarkerAsync( Action action )
    {
        const string markerName = "marker.txt";
        var created = new ConcurrentQueue<string>();
        var markerCreated = new TaskCompletionSource<bool>( TaskCreationOptions.RunContinuationsAsynchronously );

        using ( this.WatchedFileSystem.WatchChanges(
                   this.Directory,
                   "*",
                   args =>
                   {
                       if ( args.ChangeType != WatcherChangeTypes.Created )
                       {
                           return;
                       }

                       if ( args.Name == markerName )
                       {
                           markerCreated.TrySetResult( true );
                       }
                       else
                       {
                           created.Enqueue( args.Name! );
                       }
                   } ) )
        {
            action();
            this.WatchedFileSystem.WriteAllText( Path.Combine( this.Directory, markerName ), "marker" );

            await this.WaitAsync( markerCreated, () => $"The marker was not reported. Other creations: [{string.Join( ", ", created )}]." );

            return created.ToArray();
        }
    }

    /// <summary>
    /// Waits for a task, failing the test when <see cref="_timeout"/> elapses first.
    /// </summary>
    protected async Task<T> WaitAsync<T>( TaskCompletionSource<T> completionSource, Func<string> describeFailure )
    {
        using ( this._timeout.Token.Register( () => completionSource.TrySetCanceled() ) )
        {
            try
            {
                return await completionSource.Task;
            }
            catch ( TaskCanceledException )
            {
                Assert.Fail( describeFailure() );

                throw;
            }
        }
    }

    /// <summary>
    /// Watches <see cref="Directory"/> for <c>*.json</c> files, runs an action, and returns the first
    /// <see cref="WatcherChangeTypes.Renamed"/> event it raises.
    /// </summary>
    protected async Task<RenamedEventArgs> WatchUntilRenamedAsync( Action action )
    {
        var renamed = new TaskCompletionSource<RenamedEventArgs>( TaskCreationOptions.RunContinuationsAsynchronously );
        var otherEvents = new ConcurrentQueue<WatcherChangeTypes>();

        using ( this.WatchedFileSystem.WatchChanges(
                   this.Directory,
                   "*.json",
                   args =>
                   {
                       if ( args is RenamedEventArgs renamedArgs )
                       {
                           renamed.TrySetResult( renamedArgs );
                       }
                       else
                       {
                           otherEvents.Enqueue( args.ChangeType );
                       }
                   } ) )
        {
            action();

            return await this.WaitAsync( renamed, () => $"No Renamed event was raised. Other events: [{string.Join( ", ", otherEvents )}]." );
        }
    }

    public virtual void Dispose() => this._timeout.Dispose();
}

/// <summary>
/// Runs <see cref="WatchChangesTests"/> against <see cref="TestFileSystem"/>.
/// </summary>
public sealed class TestFileSystemWatchChangesTests : WatchChangesTests
{
    public TestFileSystemWatchChangesTests( ITestOutputHelper logger ) : base( logger )
    {
        this.Directory = Path.Combine( Path.GetTempPath(), "WatchChangesTests" );
        this.FileSystem.CreateDirectory( this.Directory );
    }

    protected override IFileSystem WatchedFileSystem => this.FileSystem;

    protected override string Directory { get; }

    /// <summary>
    /// Tests that the test double reports a directory rename spelled with trailing separators. The real watcher
    /// reports the rename whatever the spelling, so a test passing such paths to the double must not see it vanish.
    /// </summary>
    [Fact]
    public async Task DirectoryRenameWithTrailingSeparatorsRaisesRenamed()
    {
        var oldPath = Path.Combine( this.Directory, "old.json" );
        var newPath = Path.Combine( this.Directory, "new.json" );
        this.FileSystem.CreateDirectory( oldPath );

        var renamed = await this.WatchUntilRenamedAsync(
            () => this.FileSystem.MoveDirectory( oldPath + Path.DirectorySeparatorChar, newPath + Path.DirectorySeparatorChar ) );

        Assert.Equal( "old.json", renamed.OldName );
        Assert.Equal( "new.json", renamed.Name );
    }

    /// <summary>
    /// Tests that the test double reports a rename whose destination contains <c>..</c> segments, which the real
    /// file system resolves before notifying the watcher of the directory it resolves to.
    /// </summary>
    [Fact]
    public async Task RenameWithDotSegmentsRaisesRenamed()
    {
        var oldPath = Path.Combine( this.Directory, "file.tmp" );
        var newPath = Path.Combine( this.Directory, "sub", "..", "file.json" );
        this.FileSystem.CreateDirectory( Path.Combine( this.Directory, "sub" ) );
        this.FileSystem.WriteAllText( oldPath, "content" );

        var renamed = await this.WatchUntilRenamedAsync( () => this.FileSystem.MoveFile( oldPath, newPath ) );

        Assert.Equal( "file.tmp", renamed.OldName );
        Assert.Equal( "file.json", renamed.Name );
    }

    /// <summary>
    /// Tests that the test double reports a rename given as paths relative to the current directory, which it
    /// resolves as it resolves the paths of every other operation.
    /// </summary>
    [Fact]
    public async Task RelativeRenameRaisesRenamed()
    {
        this.FileSystem.Mock.Directory.SetCurrentDirectory( this.Directory );
        this.FileSystem.WriteAllText( "file.tmp", "content" );

        var renamed = await this.WatchUntilRenamedAsync( () => this.FileSystem.MoveFile( "file.tmp", "file.json" ) );

        Assert.Equal( "file.tmp", renamed.OldName );
        Assert.Equal( "file.json", renamed.Name );
    }

    /// <summary>
    /// Tests that the test double reports a move to another directory as the real watcher does, as a deletion from
    /// the source directory and a creation in the destination one, and not as a rename that neither watcher of the
    /// real file system would receive.
    /// </summary>
    [Fact]
    public async Task MoveToAnotherDirectoryRaisesDeletedAndCreated()
    {
        var otherDirectory = Path.Combine( Path.GetTempPath(), "WatchChangesTests.Other" );
        this.FileSystem.CreateDirectory( otherDirectory );

        var sourcePath = Path.Combine( this.Directory, "file.json" );
        var destinationPath = Path.Combine( otherDirectory, "file.json" );
        this.FileSystem.WriteAllText( sourcePath, "content" );

        var sourceEvent = new TaskCompletionSource<FileSystemEventArgs>( TaskCreationOptions.RunContinuationsAsynchronously );
        var destinationEvent = new TaskCompletionSource<FileSystemEventArgs>( TaskCreationOptions.RunContinuationsAsynchronously );

        using var sourceWatcher = this.FileSystem.WatchChanges( this.Directory, "*.json", args => sourceEvent.TrySetResult( args ) );
        using var destinationWatcher = this.FileSystem.WatchChanges( otherDirectory, "*.json", args => destinationEvent.TrySetResult( args ) );

        this.FileSystem.MoveFile( sourcePath, destinationPath );

        Assert.Equal( WatcherChangeTypes.Deleted, (await this.WaitAsync( sourceEvent, () => "The source directory raised no event." )).ChangeType );

        Assert.Equal(
            WatcherChangeTypes.Created,
            (await this.WaitAsync( destinationEvent, () => "The destination directory raised no event." )).ChangeType );
    }
}

/// <summary>
/// Runs <see cref="WatchChangesTests"/> against the real file system.
/// </summary>
public sealed class RealFileSystemWatchChangesTests : WatchChangesTests
{
    private readonly FileSystem _fileSystem = new();

    public RealFileSystemWatchChangesTests( ITestOutputHelper logger ) : base( logger )
    {
        this.Directory = Path.Combine( Path.GetTempPath(), "SharpCrafters.Backstage.Tests", Guid.NewGuid().ToString( "N" ) );
        System.IO.Directory.CreateDirectory( this.Directory );
    }

    protected override IFileSystem WatchedFileSystem => this._fileSystem;

    protected override string Directory { get; }

    public override void Dispose()
    {
        base.Dispose();

        try
        {
            System.IO.Directory.Delete( this.Directory, true );
        }
        catch ( IOException ) { }
        catch ( UnauthorizedAccessException ) { }
    }
}
