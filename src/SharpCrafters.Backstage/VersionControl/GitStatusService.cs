// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Application;
using SharpCrafters.Backstage.Diagnostics;
using SharpCrafters.Backstage.Extensibility;
using SharpCrafters.Backstage.Infrastructure;
using SharpCrafters.Common.Testing.Hooks;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SharpCrafters.Backstage.VersionControl;

/// <summary>
/// The implementation of <see cref="IVcsStatusService"/> that queries git.
/// </summary>
internal sealed class GitStatusService : IVcsStatusService
{
    /// <summary>
    /// The command that is run when the environment names no other one.
    /// </summary>
    internal const string DefaultGitFileName = "git";

    /// <summary>
    /// The environment variable, after the prefix of the product, that names the git command. It is
    /// <c>METALAMA_GIT_PATH</c> for Metalama.
    /// </summary>
    internal const string GitPathVariableName = "GIT_PATH";

    /// <remarks>
    /// <para>
    /// <c>--no-optional-locks</c> comes before the subcommand because it is an option of git itself. Without it,
    /// <c>git status</c> refreshes and rewrites the index, which is several build nodes writing the same file, and
    /// which would also make the modification time of the index useless as a signal that the cached record is stale.
    /// </para>
    /// <para>
    /// <c>-z</c> is not an optimization but a correctness requirement. In its absence git applies C-quoting to the
    /// paths it prints, governed by <c>core.quotepath</c>, so a path outside ASCII comes out escaped in one
    /// configuration and as raw bytes in another.
    /// </para>
    /// <para>
    /// <c>--untracked-files=no</c> spares the walk of the whole working tree, which is the expensive part of the
    /// command on a large repository, and untracked files do not count as modified anyway.
    /// </para>
    /// </remarks>
    internal const string GitArguments = "--no-optional-locks status --porcelain=v1 -z --untracked-files=no --ignore-submodules=all";

    /// <summary>
    /// The variables through which the environment can point git at another repository, another index or another
    /// working tree than the one being queried. A build started from a git hook inherits them.
    /// </summary>
    private static readonly string[] _gitEnvironmentVariables =
    [
        "GIT_DIR", "GIT_WORK_TREE", "GIT_INDEX_FILE", "GIT_COMMON_DIR", "GIT_OBJECT_DIRECTORY", "GIT_CEILING_DIRECTORIES"
    ];

    private static readonly TimeSpan _commandTimeout = TimeSpan.FromMinutes( 2 );

    private readonly IProcessExecutor _processExecutor;
    private readonly IFileSystem _fileSystem;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly ILogger _logger;
    private readonly VcsStatusCache _cache;
    private readonly string _gitFileName;

    /// <summary>
    /// The provider of the test synchronization points, which is never registered in production and is therefore
    /// normally <see langword="null"/>.
    /// </summary>
    private readonly ITestSynchronizationProvider? _testSynchronizationProvider;

    /// <summary>
    /// The repository roots being queried at this instant, so that the projects that a build node compiles in
    /// parallel share one command instead of starting one each.
    /// </summary>
    private readonly ConcurrentDictionary<string, Task<VcsStatusRecord?>> _queriesInFlight = new( PathComparer );

    /// <summary>
    /// The repository root of a directory, or <c>null</c> when the directory belongs to no repository. The map is
    /// kept because the source files of a project are spread over few directories but many files.
    /// </summary>
    private readonly ConcurrentDictionary<string, string?> _repositoryRoots = new( PathComparer );

    /// <summary>
    /// Compares paths without regard to case, on every platform.
    /// </summary>
    /// <remarks>
    /// The choice is decided by the direction in which each mistake fails, not by the semantics of the file system.
    /// Comparing without regard to case can only match a file that a case-sensitive comparison would not, which
    /// reports the project as modified and enforces licensing. Comparing with regard to case on a file system that
    /// ignores it would fail to match a genuinely modified file, and would waive the enforcement. The cost of the
    /// first mistake is borne by us, the cost of the second by the customer.
    /// </remarks>
    internal static StringComparer PathComparer => VcsStatusRecord.PathComparer;

    public GitStatusService( IServiceProvider serviceProvider )
    {
        this._processExecutor = serviceProvider.GetRequiredBackstageService<IProcessExecutor>();
        this._fileSystem = serviceProvider.GetRequiredBackstageService<IFileSystem>();
        this._dateTimeProvider = serviceProvider.GetRequiredBackstageService<IDateTimeProvider>();
        this._logger = serviceProvider.GetLoggerFactory().GetLogger( "Vcs" );
        this._cache = new VcsStatusCache( serviceProvider, this._logger );

        // Resolved untyped, because ITestSynchronizationProvider is shared with the layers above and therefore
        // cannot derive from IBackstageService.
        this._testSynchronizationProvider = (ITestSynchronizationProvider?) serviceProvider.GetService( typeof(ITestSynchronizationProvider) );

        this._gitFileName = GetGitFileName( serviceProvider, this._logger );
    }

    /// <summary>
    /// Gets the git command to run. The environment variable named by <see cref="GitPathVariableName"/>, after the
    /// prefix of the product, takes precedence over the command of the same name on the search path.
    /// </summary>
    /// <remarks>
    /// A developer can have git installed without it being on the search path, and a build agent can carry several
    /// installations. Reporting every source tree of such a machine as modified, which is what a command that cannot
    /// be started produces, is correct but unhelpful when the user knows where git is.
    /// </remarks>
    private static string GetGitFileName( IServiceProvider serviceProvider, ILogger logger )
    {
        var variableName = serviceProvider.GetRequiredBackstageService<ProductProfile>().GetEnvironmentVariableName( GitPathVariableName );

        var path = serviceProvider.GetRequiredBackstageService<IEnvironmentVariableProvider>().GetEnvironmentVariable( variableName );

        if ( string.IsNullOrWhiteSpace( path ) )
        {
            return DefaultGitFileName;
        }

        logger.Info?.Log( $"The git command is '{path}', named by the '{variableName}' environment variable." );

        return path!;
    }

    public async ValueTask<bool> IsAnyFileModifiedAsync( IReadOnlyCollection<string> filePaths, CancellationToken cancellationToken = default )
    {
        var filesByRepository = new Dictionary<string, List<string>>( PathComparer );
        var filesOutsideRepository = 0;

        foreach ( var filePath in filePaths )
        {
            cancellationToken.ThrowIfCancellationRequested();

            var fullPath = this.GetFullPath( filePath );

            if ( fullPath == null )
            {
                filesOutsideRepository++;

                continue;
            }

            var root = this.GetRepositoryRoot( fullPath );

            if ( root == null )
            {
                filesOutsideRepository++;

                continue;
            }

            if ( !filesByRepository.TryGetValue( root, out var files ) )
            {
                files = [];
                filesByRepository.Add( root, files );
            }

            files.Add( fullPath );
        }

        if ( filesOutsideRepository > 0 )
        {
            // These files are ignored rather than counted as modified, which is an accepted limit of the feature. The
            // count is reported because it is what explains an unexpected verdict in a support case.
            this._logger.Info?.Log( $"{filesOutsideRepository} of {filePaths.Count} files belong to no git repository and are ignored." );
        }

        if ( filesByRepository.Count == 0 )
        {
            // None of the files is in a repository, so there is nothing this service can answer about. That is a
            // condition of the machine or of the configuration rather than a property of the files, and the caller
            // asked a question that cannot be answered, so it is reported as an error and not as a verdict.
            throw new InvalidOperationException(
                $"None of the {filePaths.Count} files belongs to a git repository, so the version control status cannot be determined." );
        }

        foreach ( var pair in filesByRepository )
        {
            cancellationToken.ThrowIfCancellationRequested();

            var record = await this.GetStatusAsync( pair.Key, pair.Value, cancellationToken );

            if ( record == null )
            {
                // The status of this repository is unknown, and an unknown status is reported as modified.
                return true;
            }

            foreach ( var file in pair.Value )
            {
                if ( record.IsModified( file ) )
                {
                    this._logger.Info?.Log( $"The file '{file}' is modified in the git repository '{pair.Key}'." );

                    return true;
                }
            }
        }

        this._logger.Info?.Log( $"None of the {filePaths.Count} files is modified in version control." );

        return false;
    }

    private async ValueTask<VcsStatusRecord?> GetStatusAsync(
        string repositoryRoot,
        IReadOnlyCollection<string> files,
        CancellationToken cancellationToken )
    {
        var cached = await this._cache.TryGetAsync( repositoryRoot, files, cancellationToken );

        if ( cached != null )
        {
            return cached;
        }

        // A record that the command has just produced is authoritative by construction and is not submitted to the
        // staleness rule, which would otherwise reject it whenever the build has just written one of the files it
        // compiles, as it does for the sources it generates.
        //
        // The caller waits with its own token. The run itself carries none, so that a caller which gives up abandons
        // its wait without cancelling the run for the callers that did not.
        return await WaitAsync( this.QueryAsync( repositoryRoot, cancellationToken ), cancellationToken );
    }

    /// <summary>
    /// Runs the command for a repository, or joins the run that another caller has already started for it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The work is shared within the process only. Sharing it between processes would mean holding a machine-wide
    /// lock across the command, and a named lock has thread affinity and therefore cannot be held across an
    /// <c>await</c>. The file layer of the cache already limits the cost to one command per build node on a cold
    /// cache, and to none at all afterwards.
    /// </para>
    /// <para>
    /// The run carries no cancellation token. A run is shared, so a token would be the token of whichever caller
    /// happened to start it, and cancelling that caller would cancel the run for every caller that joined it and
    /// never asked to be cancelled. The command is bounded by its own timeout instead, and a caller that cancels
    /// stops waiting at once, which is what its token is for.
    /// </para>
    /// </remarks>
    private Task<VcsStatusRecord?> QueryAsync( string repositoryRoot, CancellationToken cancellationToken )
    {
        while ( true )
        {
            if ( this._queriesInFlight.TryGetValue( repositoryRoot, out var running ) )
            {
                this._testSynchronizationProvider?.SyncPoint(
                    TestSynchronizationPoints.ForService( TestSynchronizationPoints.JoinedQuery, repositoryRoot ),
                    cancellationToken );

                return running;
            }

            var query = new TaskCompletionSource<VcsStatusRecord?>( TaskCreationOptions.RunContinuationsAsynchronously );

            this._testSynchronizationProvider?.SyncPoint(
                TestSynchronizationPoints.ForService( TestSynchronizationPoints.BeforeRegisteringQuery, repositoryRoot ) );

            // The loop repeats rather than reading the entry that won the race, because that entry can be removed
            // again between the failed insertion and the read.
            if ( this._queriesInFlight.TryAdd( repositoryRoot, query.Task ) )
            {
                _ = this.RunQueryAsync( repositoryRoot, query );

                return query.Task;
            }
        }
    }

    private async Task RunQueryAsync( string repositoryRoot, TaskCompletionSource<VcsStatusRecord?> query )
    {
        try
        {
            var record = await this.RunGitAsync( repositoryRoot, CancellationToken.None );

            if ( record != null )
            {
                await this._cache.SetAsync( repositoryRoot, record, CancellationToken.None );
            }

            query.TrySetResult( record );
        }
        catch ( OperationCanceledException )
        {
            query.TrySetCanceled();
        }
        catch ( Exception e )
        {
            query.TrySetException( e );
        }
        finally
        {
            // The entry is removed once the query has completed, so that a later build in the same process observes
            // the state of the repository at that later time rather than this one.
            this._queriesInFlight.TryRemove( repositoryRoot, out _ );
        }
    }

    /// <summary>
    /// Awaits a task with a token that belongs to the caller rather than to the task, so that the caller stops waiting
    /// without the task being cancelled for anybody else.
    /// </summary>
    private static async Task<T> WaitAsync<T>( Task<T> task, CancellationToken cancellationToken )
    {
        if ( task.IsCompleted || !cancellationToken.CanBeCanceled )
        {
            return await task;
        }

        var cancelled = new TaskCompletionSource<bool>( TaskCreationOptions.RunContinuationsAsynchronously );

        using ( cancellationToken.Register( () => cancelled.TrySetCanceled( cancellationToken ) ) )
        {
            if ( await Task.WhenAny( task, cancelled.Task ) != task )
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
        }

        return await task;
    }

    private async Task<VcsStatusRecord?> RunGitAsync( string repositoryRoot, CancellationToken cancellationToken )
    {
        // The instant is taken before the command starts. See the remarks of VcsStatusRecord.
        var timestamp = this._dateTimeProvider.UtcNow;

        var startInfo = new ProcessStartInfo( this._gitFileName, GitArguments ) { WorkingDirectory = repositoryRoot };

        // The output is decoded as UTF-8 rather than with the code page of the console, which is what the process
        // component uses by default and which would corrupt every path outside ASCII.
        startInfo.StandardOutputEncoding = new UTF8Encoding( false );

        foreach ( var variable in _gitEnvironmentVariables )
        {
            // Removed rather than set to an empty string: git distinguishes a variable that is set to nothing from
            // one that is not set, and an empty GIT_DIR makes it refuse to run at all.
            startInfo.Environment.Remove( variable );
        }

        string? output;

        try
        {
            if ( this._testSynchronizationProvider != null )
            {
                await this._testSynchronizationProvider.SyncPointAsync(
                    TestSynchronizationPoints.ForService( TestSynchronizationPoints.InsideCommand, repositoryRoot ),
                    cancellationToken );
            }

            output = await this._processExecutor.TryExecuteAsync( startInfo, _commandTimeout, cancellationToken );
        }
        catch ( OperationCanceledException )
        {
            throw;
        }
        catch ( Exception e )
        {
            // This catch is what handles git not being installed: starting a process that does not exist throws
            // rather than returning a failure.
            this._logger.Info?.Log( $"The '{this._gitFileName}' command could not be run in '{repositoryRoot}': {e.Message}" );

            return null;
        }

        if ( output == null )
        {
            this._logger.Info?.Log( $"The '{this._gitFileName}' command did not complete successfully in '{repositoryRoot}'." );

            return null;
        }

        this._logger.Trace?.Log( $"git status of '{repositoryRoot}':{Environment.NewLine}{output.Replace( '\0', '\n' )}" );

        List<string> modifiedFiles;

        try
        {
            modifiedFiles = ParseStatus( output, repositoryRoot );
        }
        catch ( Exception e )
        {
            // A repository can hold a path that is not a legal path on the current operating system, for instance a
            // file named 'a:b.cs' committed from Unix and read on Windows, and Path.Combine rejects it on .NET
            // Framework. The status of the repository is then unknown, which enforces licensing; letting the
            // exception escape would instead fail the build.
            this._logger.Info?.Log( $"The status of '{repositoryRoot}' could not be read: {e.Message}" );

            return null;
        }

        return new VcsStatusRecord( timestamp, modifiedFiles );
    }

    /// <summary>
    /// Extracts the modified files from the output of <c>git status --porcelain=v1 -z</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Only a status in which one of the two columns is <c>M</c> counts, which is a content modification of a tracked
    /// file, staged or unstaged. Additions, deletions, renames, copies and type changes do not count, because the
    /// files that a build generates and the files that a package restores come and go without the user having
    /// modified anything.
    /// </para>
    /// <para>
    /// With <c>-z</c>, a rename or a copy is two records rather than one: the new path and then the old one. The
    /// second record is consumed even though these statuses are ignored, because a parser that did not consume it
    /// would read a path as the status of the next entry and misread everything that follows.
    /// </para>
    /// </remarks>
    internal static List<string> ParseStatus( string output, string repositoryRoot )
    {
        var modifiedFiles = new List<string>();
        var entries = output.Split( '\0' );

        for ( var i = 0; i < entries.Length; i++ )
        {
            var entry = entries[i];

            // The last record is followed by a terminator, so the split yields a trailing empty string.
            if ( entry.Length < 4 )
            {
                continue;
            }

            var index = entry[0];
            var workingTree = entry[1];
            var path = entry.Substring( 3 );

            if ( index is 'R' or 'C' )
            {
                // Consume the original path of the rename or the copy.
                i++;
            }

            if ( index == 'M' || workingTree == 'M' )
            {
                modifiedFiles.Add( Path.Combine( repositoryRoot, path.Replace( '/', Path.DirectorySeparatorChar ) ) );
            }
        }

        return modifiedFiles;
    }

    private string? GetFullPath( string filePath )
    {
        try
        {
            return Path.GetFullPath( filePath );
        }
        catch ( Exception e )
        {
            this._logger.Trace?.Log( $"The path '{filePath}' could not be resolved: {e.Message}" );

            return null;
        }
    }

    /// <summary>
    /// Finds the repository that a file belongs to by walking up from its directory until a <c>.git</c> entry is
    /// found, or returns <c>null</c> when there is none.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A <c>.git</c> entry is a directory in a normal clone and a file in a submodule and in a linked working tree,
    /// so both are accepted.
    /// </para>
    /// <para>
    /// The walk is used rather than <c>git rev-parse --show-toplevel</c>, which would be the obvious alternative,
    /// because that command reports the resolved physical path: symbolic links followed, substituted drives and
    /// junctions expanded. The paths that the caller passes are the ones its build system gave it, and combining the
    /// paths reported by git onto a resolved root would produce paths that never compare equal to them. A file that
    /// fails to compare equal is a file that is not found among the modified ones, which waives the check. The walk
    /// keeps both sides of the comparison in the spelling of the caller.
    /// </para>
    /// </remarks>
    private string? GetRepositoryRoot( string fullPath )
    {
        var directory = Path.GetDirectoryName( fullPath );

        if ( string.IsNullOrEmpty( directory ) )
        {
            return null;
        }

        if ( this._repositoryRoots.TryGetValue( directory!, out var cachedRoot ) )
        {
            return cachedRoot;
        }

        string? root = null;

        for ( var candidate = directory; !string.IsNullOrEmpty( candidate ); candidate = Path.GetDirectoryName( candidate ) )
        {
            var gitPath = Path.Combine( candidate!, ".git" );

            if ( this._fileSystem.DirectoryExists( gitPath ) || this._fileSystem.FileExists( gitPath ) )
            {
                root = candidate;

                break;
            }
        }

        this._repositoryRoots[directory!] = root;

        return root;
    }
}