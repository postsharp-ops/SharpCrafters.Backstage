// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Diagnostics;
using SharpCrafters.Backstage.Extensibility;
using SharpCrafters.Backstage.Infrastructure;
using SharpCrafters.Backstage.Maintenance;
using SharpCrafters.Backstage.Utilities;
using SharpCrafters.Common;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SharpCrafters.Backstage.VersionControl;

/// <summary>
/// The two-layer cache of <see cref="GitStatusService"/>, keyed by repository root: an in-memory layer scoped to the
/// current process, and a file layer shared by every process on the machine.
/// </summary>
/// <remarks>
/// <para>
/// The two layers hold the same <see cref="VcsStatusRecord"/> and are governed by the same predicate,
/// <see cref="IsValid"/>, so that they cannot drift into two different caching policies. The in-memory layer spares
/// the projects that one build node compiles after the first; the file layer spares the other build nodes and the
/// subsequent builds.
/// </para>
/// <para>
/// The in-memory layer is deliberately not a permanent memo. A build node is reused and can live for hours, so a
/// record kept for the lifetime of the process would report a source tree as unmodified across a whole editing
/// session.
/// </para>
/// <para>
/// Every failure of this class is a cache miss and a trace record. A miss costs one version control command; an
/// exception would fail the build, and a wrong hit would waive the check.
/// </para>
/// </remarks>
internal sealed class VcsStatusCache
{
    /// <summary>
    /// The first line of the file, which makes a file written by another version of this class a miss rather than a
    /// parse error.
    /// </summary>
    private const string _formatVersion = "vcscache/1";

    /// <summary>
    /// How long before <see cref="VcsStatusRecord.Timestamp"/> a file must have been written for the record to be
    /// considered to cover it. It absorbs the two-second granularity of the modification times of FAT, exFAT and SMB
    /// volumes, on which a file written just before the query would otherwise appear to predate it.
    /// </summary>
    private static readonly TimeSpan _timestampMargin = TimeSpan.FromSeconds( 2 );

    private readonly IFileSystem _fileSystem;
    private readonly ILogger _logger;

    /// <summary>
    /// The provider of the test synchronization points, which is never registered in production and is therefore
    /// normally <see langword="null"/>.
    /// </summary>
    private readonly ITestSynchronizationProvider? _testSynchronizationProvider;
    private readonly ConcurrentDictionary<string, VcsStatusRecord> _memory = new( StringComparer.OrdinalIgnoreCase );
    private readonly Lazy<string?> _directory;

    public VcsStatusCache( IServiceProvider serviceProvider, ILogger logger )
    {
        this._fileSystem = serviceProvider.GetRequiredBackstageService<IFileSystem>();
        this._logger = logger;

        // Resolved untyped, because ITestSynchronizationProvider is shared with the layers above and therefore
        // cannot derive from IBackstageService.
        this._testSynchronizationProvider = (ITestSynchronizationProvider?) serviceProvider.GetService( typeof(ITestSynchronizationProvider) );

        // The directory is resolved lazily, because creating it is an operation of the file system and this class is
        // constructed whether or not the check is ever performed.
        this._directory = new Lazy<string?>(
            () =>
            {
                try
                {
                    return serviceProvider.GetRequiredBackstageService<ITempFileManager>()
                        .GetTempDirectory( "VcsStatusCache", CleanUpStrategy.FileOneMonthAfterCreation );
                }
                catch ( Exception e )
                {
                    this._logger.Warning?.Log( $"The version control cache directory is not available: {e.Message}" );

                    return null;
                }
            } );
    }

    /// <summary>
    /// Gets the record of a repository from the memory layer and then from the file layer, and returns it only when
    /// it is valid for the given files.
    /// </summary>
    public async ValueTask<VcsStatusRecord?> TryGetAsync(
        string repositoryRoot,
        IReadOnlyCollection<string> files,
        CancellationToken cancellationToken )
    {
        if ( this._memory.TryGetValue( repositoryRoot, out var record ) && this.IsValid( record, repositoryRoot, files ) )
        {
            this._logger.Trace?.Log( $"The status of '{repositoryRoot}' is taken from the memory cache." );

            return record;
        }

        if ( this._testSynchronizationProvider != null )
        {
            await this._testSynchronizationProvider.SyncPointAsync(
                TestSynchronizationPoints.ForCache( TestSynchronizationPoints.BeforeReadingFile, repositoryRoot ),
                cancellationToken );
        }

        var stored = await this.TryReadAsync( repositoryRoot, cancellationToken );

        if ( stored == null || !this.IsValid( stored, repositoryRoot, files ) )
        {
            return null;
        }

        this._logger.Trace?.Log( $"The status of '{repositoryRoot}' is taken from the file cache." );

        // The record is promoted, so that the next project compiled by this process does not read the file again.
        this._memory[repositoryRoot] = stored;

        return stored;
    }

    /// <summary>
    /// Stores a record that a fresh query has just produced, in both layers.
    /// </summary>
    public async ValueTask SetAsync( string repositoryRoot, VcsStatusRecord record, CancellationToken cancellationToken )
    {
        this._memory[repositoryRoot] = record;

        if ( this._testSynchronizationProvider != null )
        {
            await this._testSynchronizationProvider.SyncPointAsync(
                TestSynchronizationPoints.ForCache( TestSynchronizationPoints.BeforeWritingFile, repositoryRoot ),
                cancellationToken );
        }

        await this.TryWriteAsync( repositoryRoot, record, cancellationToken );
    }

    /// <summary>
    /// Determines whether a record that was produced in the past still describes the current state of the given files.
    /// </summary>
    /// <remarks>
    /// This is never applied to a record that the version control tool has just produced. Such a record is
    /// authoritative by construction, and the margin below would reject it whenever the build itself has just written
    /// one of the files, which is what a build does to the sources it generates.
    /// </remarks>
    private bool IsValid( VcsStatusRecord record, string repositoryRoot, IReadOnlyCollection<string> files )
    {
        var threshold = record.Timestamp - _timestampMargin;

        // The index and the head are what a branch switch, a reset, a stash, a staging or a rebase touches. These
        // change the answer of the query without necessarily changing the modification time of any source file, so
        // the source files alone are not enough to detect that the record is stale.
        foreach ( var gitFile in new[] { "index", "HEAD" } )
        {
            var path = Path.Combine( repositoryRoot, ".git", gitFile );

            if ( this.GetLastWriteTimeUtc( path ) >= threshold )
            {
                this._logger.Trace?.Log( $"The cached status of '{repositoryRoot}' is stale because '.git/{gitFile}' has changed." );

                return false;
            }
        }

        foreach ( var file in files )
        {
            // A file that no longer exists reports a modification time in the distant past rather than throwing, and
            // therefore does not invalidate the record. That is the intended outcome: a deletion is not a
            // modification under the doctrine of this feature.
            if ( this.GetLastWriteTimeUtc( file ) >= threshold )
            {
                this._logger.Trace?.Log( $"The cached status of '{repositoryRoot}' is stale because '{file}' has been written." );

                return false;
            }
        }

        return true;
    }

    private DateTime GetLastWriteTimeUtc( string path )
    {
        // A file that does not exist is reported as infinitely old rather than being asked for its modification time.
        // That is deliberate for the two cases in which it happens: a source file that has been deleted, which is not
        // a modification under the doctrine of this feature, and '.git/index' or '.git/HEAD' in a linked working tree,
        // where the real directory is elsewhere and its absence here says nothing.
        if ( !this._fileSystem.FileExists( path ) )
        {
            return DateTime.MinValue;
        }

        try
        {
            // The file system service reports local time, and a comparison that mixed it with a UTC timestamp would
            // be wrong by the offset of the time zone, and would change across a daylight saving transition.
            return this._fileSystem.GetFileLastWriteTime( path ).ToUniversalTime();
        }
        catch ( Exception e )
        {
            this._logger.Trace?.Log( $"The modification time of '{path}' is not available: {e.Message}" );

            // An unreadable modification time invalidates the record, which costs one version control command.
            return DateTime.MaxValue;
        }
    }

    private string? GetFilePath( string repositoryRoot )
    {
        var directory = this._directory.Value;

        if ( directory == null )
        {
            return null;
        }

        return Path.Combine( directory, $"git@{HashUtilities.HashToString( repositoryRoot )}.vcscache" );
    }

    private async ValueTask<VcsStatusRecord?> TryReadAsync( string repositoryRoot, CancellationToken cancellationToken )
    {
        var path = this.GetFilePath( repositoryRoot );

        if ( path == null || !this._fileSystem.FileExists( path ) )
        {
            return null;
        }

        string content;

        try
        {
            content = await this._fileSystem.ReadAllTextAsync( path, cancellationToken );
        }
        catch ( Exception e ) when ( e is not OperationCanceledException )
        {
            // The file is substituted rather than rewritten, so a reader can meet it while it is being replaced.
            this._logger.Trace?.Log( $"The version control cache of '{repositoryRoot}' could not be read: {e.Message}" );

            return null;
        }

        return this.Parse( content, repositoryRoot );
    }

    private VcsStatusRecord? Parse( string content, string repositoryRoot )
    {
        var lines = content.Split( '\n' );

        if ( lines.Length < 2 || lines[0].TrimEnd( '\r' ) != _formatVersion )
        {
            this._logger.Trace?.Log( $"The version control cache of '{repositoryRoot}' is not in a known format." );

            return null;
        }

        if ( !DateTime.TryParseExact(
                lines[1].TrimEnd( '\r' ),
                "O",
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var timestamp ) )
        {
            this._logger.Trace?.Log( $"The version control cache of '{repositoryRoot}' has no readable timestamp." );

            return null;
        }

        var modifiedFiles = lines
            .Skip( 2 )
            .Select( line => line.TrimEnd( '\r' ) )
            .Where( line => line.Length > 0 )
            .ToList();

        return new VcsStatusRecord( timestamp.ToUniversalTime(), modifiedFiles );
    }

    private async ValueTask TryWriteAsync( string repositoryRoot, VcsStatusRecord record, CancellationToken cancellationToken )
    {
        var path = this.GetFilePath( repositoryRoot );

        if ( path == null )
        {
            return;
        }

        // A path is one line of the file, so a path that contains a line break cannot be stored without an escaping
        // scheme. Such a path is legal on Unix and vanishingly rare, and the only cost of not storing the record is
        // that the repository is queried again, so the record is dropped rather than escaped.
        if ( record.ModifiedFiles.Any( f => f.Any( c => c is '\n' or '\r' ) ) )
        {
            this._logger.Trace?.Log( $"The status of '{repositoryRoot}' is not cached because a path contains a line break." );

            return;
        }

        var builder = new StringBuilder();
        builder.Append( _formatVersion ).Append( '\n' );
        builder.Append( record.Timestamp.ToString( "O", CultureInfo.InvariantCulture ) ).Append( '\n' );

        foreach ( var file in record.ModifiedFiles )
        {
            builder.Append( file ).Append( '\n' );
        }

        try
        {
            // The write is atomic, so a concurrent reader never observes a truncated list. A truncated list would be
            // the dangerous outcome: it parses as a valid record in which a modified file is simply missing.
            await this._fileSystem.WriteAllTextAtomicallyAsync( path, builder.ToString(), cancellationToken );
        }
        catch ( Exception e ) when ( e is not OperationCanceledException )
        {
            this._logger.Warning?.Log( $"The version control cache of '{repositoryRoot}' could not be written: {e.Message}" );
        }
    }
}
