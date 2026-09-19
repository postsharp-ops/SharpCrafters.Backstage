// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Extensibility;
using SharpCrafters.Backstage.Testing;
using SharpCrafters.Backstage.VersionControl;
using SharpCrafters.Common;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace SharpCrafters.Backstage.Tests.VersionControl;

/// <summary>
/// Tests of what happens when several callers query the version control status at the same time, which is the normal
/// case: a build node compiles the projects of a solution in parallel and each one asks about the same repository.
/// </summary>
/// <remarks>
/// <para>
/// Every interleaving here is driven through <see cref="ITestSynchronizationProvider"/> rather than waited for, so no
/// assertion depends on a duration and none of these tests can pass by accident on a fast machine. What they protect
/// is the promise that the repository is queried once rather than once per project, and that the sharing never
/// produces a verdict the caller did not ask for.
/// </para>
/// <para>
/// Each caller runs on a dedicated thread rather than on the thread pool. A caller held at a synchronization point
/// blocks its thread, and callers held on pool threads exhaust the pool, after which the test that comes next cannot
/// start and the whole run stops rather than failing.
/// </para>
/// </remarks>
public sealed class VcsStatusConcurrencyTests : TestsBase, IDisposable
{
    private static readonly string _repository = Path.GetFullPath( Path.Combine( Path.GetTempPath(), "repo" ) );
    private static readonly string _otherRepository = Path.GetFullPath( Path.Combine( Path.GetTempPath(), "other" ) );

    private readonly TestSynchronizationProvider _sync;
    private readonly CancellationTokenSource _timeout = new( TimeSpan.FromSeconds( 30 ) );

    public VcsStatusConcurrencyTests( ITestOutputHelper logger ) : base( logger )
    {
        this._sync = new TestSynchronizationProvider( logger.WriteLine );
    }

    protected override void ConfigureServices( ServiceProviderBuilder services )
        => services.AddService( typeof(ITestSynchronizationProvider), this._sync );

    public void Dispose()
    {
        // Releases whatever is still held, so that a failed assertion does not leave a thread blocked for ever.
        this._sync.Dispose();
        this._timeout.Dispose();
    }

    private GitStatusService CreateService() => new( this.ServiceProvider );

    private static string InsideCommand( string root )
        => TestSynchronizationPoints.ForService( TestSynchronizationPoints.InsideCommand, root );

    private static string JoinedQuery( string root )
        => TestSynchronizationPoints.ForService( TestSynchronizationPoints.JoinedQuery, root );

    private static string BeforeRegistering( string root )
        => TestSynchronizationPoints.ForService( TestSynchronizationPoints.BeforeRegisteringQuery, root );

    private static string BeforeWritingFile( string root )
        => TestSynchronizationPoints.ForCache( TestSynchronizationPoints.BeforeWritingFile, root );

    private static string BeforeReadingFile( string root )
        => TestSynchronizationPoints.ForCache( TestSynchronizationPoints.BeforeReadingFile, root );

    private void CreateRepository( string root )
    {
        this.FileSystem.CreateDirectory( Path.Combine( root, ".git" ) );
        this.CreateFileInThePast( Path.Combine( root, ".git", "HEAD" ), "ref: refs/heads/main" );
        this.CreateFileInThePast( Path.Combine( root, ".git", "index" ), "index" );
    }

    /// <summary>
    /// Writes a file and dates it an hour ago. A record never covers a file written at or after the instant it was
    /// made, so a repository whose files carry the current time admits no cache hit at all.
    /// </summary>
    private string CreateFileInThePast( string path, string content = "content" )
    {
        this.WriteFile( path, content );
        this.FileSystem.SetFileLastWriteTime( path, this.Time.UtcNow.AddHours( -1 ).ToLocalTime() );

        return path;
    }

    private string WriteFile( string path, string content = "content" )
    {
        var directory = Path.GetDirectoryName( path )!;

        if ( !this.FileSystem.DirectoryExists( directory ) )
        {
            this.FileSystem.CreateDirectory( directory );
        }

        this.FileSystem.WriteAllText( path, content );

        return path;
    }

    /// <summary>
    /// Declares a source file, written in the past so that it does not fall inside the margin that invalidates a
    /// cached record.
    /// </summary>
    private string CreateSourceFile( string root, string relativePath )
        => this.CreateFileInThePast( Path.Combine( root, relativePath.Replace( '/', Path.DirectorySeparatorChar ) ) );

    private void SetGitOutput( string output ) => this.ProcessExecutor.StandardOutputProvider = _ => output;

    /// <summary>
    /// Starts a query on a dedicated thread. See the remarks on this class for why the thread pool is not used.
    /// </summary>
    private static Task<bool> QueryAsync( GitStatusService service, string file, CancellationToken cancellationToken = default )
        => Task.Factory.StartNew(
                () => service.IsAnyFileModifiedAsync( [file], cancellationToken ).AsTask(),
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default )
            .Unwrap();

    /// <summary>
    /// Waits until the code under test reaches a synchronization point, failing rather than hanging if it never does.
    /// </summary>
    private Task ReachedAsync( string syncPointName ) => this.WithTimeout( this._sync.WaitForSyncPointReachedAsync( syncPointName, this._timeout.Token ) );

    /// <summary>
    /// Awaits a task, failing rather than hanging if the test takes longer than its budget. A hanging test stops the
    /// whole run, while a failing one names the test that is wrong.
    /// </summary>
    private async Task<T> WithTimeout<T>( Task<T> task )
    {
        await this.WithTimeout( (Task) task );

        return await task;
    }

    private async Task WithTimeout( Task task )
    {
        var timedOut = new TaskCompletionSource<bool>( TaskCreationOptions.RunContinuationsAsynchronously );

        using ( this._timeout.Token.Register( () => timedOut.TrySetResult( true ) ) )
        {
            if ( await Task.WhenAny( task, timedOut.Task ) != task )
            {
                throw new TimeoutException( "The test timed out while waiting for the code under test." );
            }
        }

        await task;
    }

    /// <summary>
    /// Verifies that a caller arriving while a command is running joins it instead of starting one of its own. This is
    /// the promise that makes the feature affordable: the projects of a solution ask about one repository and the
    /// repository is queried once.
    /// </summary>
    [Fact]
    public async Task ASecondCallerJoinsTheRunningCommand()
    {
        this.CreateRepository( _repository );
        var file = this.CreateSourceFile( _repository, "src/Class1.cs" );
        this.SetGitOutput( "" );

        var service = this.CreateService();

        this._sync.EnableSyncPoint( InsideCommand( _repository ) );
        this._sync.EnableSyncPoint( JoinedQuery( _repository ) );

        var first = QueryAsync( service, file );
        await this.ReachedAsync( InsideCommand( _repository ) );

        // The first caller is now inside the command. The second must find the run in progress and join it.
        var second = QueryAsync( service, file );
        await this.ReachedAsync( JoinedQuery( _repository ) );

        this._sync.DisableSyncPoint( JoinedQuery( _repository ) );
        this._sync.DisableSyncPoint( InsideCommand( _repository ) );

        Assert.False( await this.WithTimeout( first ) );
        Assert.False( await this.WithTimeout( second ) );
        Assert.Single( this.ProcessExecutor.StartedProcesses );
    }

    /// <summary>
    /// Verifies that two callers which both find no run in progress still produce one command. One of them registers
    /// its query and the other, whose registration fails, repeats its lookup and joins the winner. Without the repeated
    /// lookup the loser would read an entry that has since been removed.
    /// </summary>
    [Fact]
    public async Task ARegistrationRaceProducesOneCommand()
    {
        this.CreateRepository( _repository );
        var file = this.CreateSourceFile( _repository, "src/Class1.cs" );
        this.SetGitOutput( "" );

        var service = this.CreateService();

        this._sync.EnableSyncPoint( BeforeRegistering( _repository ) );
        this._sync.EnableSyncPoint( InsideCommand( _repository ) );
        this._sync.EnableSyncPoint( JoinedQuery( _repository ) );

        // Both callers are held after their lookup has missed and before either has registered, which is the
        // interleaving that the repeated lookup exists for and that does not reproduce on demand otherwise.
        var first = QueryAsync( service, file );
        await this.ReachedAsync( BeforeRegistering( _repository ) );

        var second = QueryAsync( service, file );
        await this.ReachedAsync( BeforeRegistering( _repository ) );

        this._sync.DisableSyncPoint( BeforeRegistering( _repository ) );

        // The winner is inside the command and the loser has joined it.
        await this.ReachedAsync( InsideCommand( _repository ) );
        await this.ReachedAsync( JoinedQuery( _repository ) );

        this._sync.DisableSyncPoint( JoinedQuery( _repository ) );
        this._sync.DisableSyncPoint( InsideCommand( _repository ) );

        Assert.False( await this.WithTimeout( first ) );
        Assert.False( await this.WithTimeout( second ) );
        Assert.Single( this.ProcessExecutor.StartedProcesses );
    }

    /// <summary>
    /// Verifies that many callers arriving while one command runs all join it. A build node compiles far more than two
    /// projects at a time, and the sharing has to hold for all of them rather than for the first one that arrives.
    /// </summary>
    [Fact]
    public async Task ManyCallersJoinOneCommand()
    {
        // Below the number of threads that TestSynchronizationProvider releases when a synchronization point is
        // released wholesale. Blocking more than that at one point leaves the excess blocked for ever.
        const int joiners = 8;

        this.CreateRepository( _repository );
        var file = this.CreateSourceFile( _repository, "src/Class1.cs" );
        this.SetGitOutput( "" );

        var service = this.CreateService();

        this._sync.EnableSyncPoint( InsideCommand( _repository ) );
        this._sync.EnableSyncPoint( JoinedQuery( _repository ) );

        var first = QueryAsync( service, file );
        await this.ReachedAsync( InsideCommand( _repository ) );

        var others = Enumerable.Range( 0, joiners ).Select( _ => QueryAsync( service, file ) ).ToList();

        for ( var i = 0; i < joiners; i++ )
        {
            await this.ReachedAsync( JoinedQuery( _repository ) );
        }

        this._sync.DisableSyncPoint( JoinedQuery( _repository ) );
        this._sync.DisableSyncPoint( InsideCommand( _repository ) );

        Assert.False( await this.WithTimeout( first ) );

        foreach ( var other in others )
        {
            Assert.False( await this.WithTimeout( other ) );
        }

        Assert.Single( this.ProcessExecutor.StartedProcesses );
    }

    /// <summary>
    /// Verifies that a query of one repository does not wait for a query of another. The shared run is keyed by
    /// repository root, and a solution spread over two repositories would otherwise serialize on the slower one.
    /// </summary>
    [Fact]
    public async Task AQueryOfAnotherRepositoryIsNotBlocked()
    {
        this.CreateRepository( _repository );
        this.CreateRepository( _otherRepository );
        var file = this.CreateSourceFile( _repository, "src/Class1.cs" );
        var otherFile = this.CreateSourceFile( _otherRepository, "src/Class2.cs" );
        this.SetGitOutput( "" );

        var service = this.CreateService();

        this._sync.EnableSyncPoint( InsideCommand( _repository ) );

        var blocked = QueryAsync( service, file );
        await this.ReachedAsync( InsideCommand( _repository ) );

        // This has to complete while the other repository is still held inside its command.
        Assert.False( await this.WithTimeout( QueryAsync( service, otherFile ) ) );

        this._sync.DisableSyncPoint( InsideCommand( _repository ) );

        Assert.False( await this.WithTimeout( blocked ) );
        Assert.Equal( 2, this.ProcessExecutor.StartedProcesses.Count );
    }

    /// <summary>
    /// Verifies that a caller cancelled while the command is running is told so, rather than being handed the verdict
    /// that the run is about to produce. A query that was abandoned has no answer: reporting "modified" would enforce
    /// licensing on a build the user has stopped, and reporting "not modified", which is what the command here would
    /// say, would waive it on the strength of an answer nobody waited for.
    /// </summary>
    /// <remarks>
    /// The command is held at a synchronization point for the whole of the cancellation, which is what makes the
    /// outcome the same on every machine. An earlier version of this test cancelled from inside the process executor
    /// and let the command complete immediately afterwards, so the caller reported the cancellation only when it
    /// happened to observe the token before the run completed, and the test failed on whichever machine lost that
    /// race.
    /// </remarks>
    [Fact]
    public async Task ACancelledCallerIsNotGivenAVerdict()
    {
        this.CreateRepository( _repository );
        var file = this.CreateSourceFile( _repository, "src/Class1.cs" );

        // The command would answer "not modified", which is the verdict the cancelled caller must not receive.
        this.SetGitOutput( "" );

        using var cancellationTokenSource = new CancellationTokenSource();

        this._sync.EnableSyncPoint( InsideCommand( _repository ) );

        var cancelled = QueryAsync( this.CreateService(), file, cancellationTokenSource.Token );
        await this.ReachedAsync( InsideCommand( _repository ) );

        cancellationTokenSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>( async () => await this.WithTimeout( cancelled ) );

        this._sync.DisableSyncPoint( InsideCommand( _repository ) );
    }

    /// <summary>
    /// Verifies that a cancelled query leaves nothing behind that a later caller would join. A caller that joined a
    /// cancelled run would be told the build was cancelled when it was not.
    /// </summary>
    [Fact]
    public async Task ACancelledQueryDoesNotPoisonTheNextOne()
    {
        this.CreateRepository( _repository );
        var file = this.CreateSourceFile( _repository, "src/Class1.cs" );
        this.SetGitOutput( "" );

        var service = this.CreateService();

        using var cancellationTokenSource = new CancellationTokenSource();

        this._sync.EnableSyncPoint( InsideCommand( _repository ) );

        var cancelled = QueryAsync( service, file, cancellationTokenSource.Token );
        await this.ReachedAsync( InsideCommand( _repository ) );

        cancellationTokenSource.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>( async () => await this.WithTimeout( cancelled ) );

        this._sync.DisableSyncPoint( InsideCommand( _repository ) );

        // A fresh caller has to obtain a verdict of its own. If the cancelled run were still registered, this would
        // join it and be cancelled too.
        Assert.False( await this.WithTimeout( QueryAsync( service, file ) ) );
    }

    /// <summary>
    /// Verifies that cancelling one caller does not cancel the callers that joined its run. A run is shared, so a
    /// token belonging to whichever caller happened to start it would cancel the work of projects that never asked to
    /// be cancelled, and in a build those projects would fail.
    /// </summary>
    [Fact]
    public async Task CancellingOneCallerDoesNotCancelTheOthers()
    {
        this.CreateRepository( _repository );
        var file = this.CreateSourceFile( _repository, "src/Class1.cs" );
        this.SetGitOutput( "" );

        var service = this.CreateService();

        using var cancellationTokenSource = new CancellationTokenSource();

        this._sync.EnableSyncPoint( InsideCommand( _repository ) );
        this._sync.EnableSyncPoint( JoinedQuery( _repository ) );

        var cancelled = QueryAsync( service, file, cancellationTokenSource.Token );
        await this.ReachedAsync( InsideCommand( _repository ) );

        // The second caller joins the run started by the first, and carries no token of its own.
        var joined = QueryAsync( service, file );
        await this.ReachedAsync( JoinedQuery( _repository ) );
        this._sync.DisableSyncPoint( JoinedQuery( _repository ) );

        // The caller that started the run gives up. The run belongs to no caller, so it continues.
        cancellationTokenSource.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>( async () => await this.WithTimeout( cancelled ) );

        this._sync.DisableSyncPoint( InsideCommand( _repository ) );

        Assert.False( await this.WithTimeout( joined ) );
        Assert.Single( this.ProcessExecutor.StartedProcesses );
    }

    /// <summary>
    /// Verifies that a second process does not read a record that the first process has not finished storing. The
    /// record reaches the memory layer before the file, and a reader that treated the absent file as an answer would
    /// be reading a state that no query ever produced.
    /// </summary>
    [Fact]
    public async Task ARecordIsNotVisibleToAnotherProcessBeforeItIsStored()
    {
        this.CreateRepository( _repository );
        var file = this.CreateSourceFile( _repository, "src/Class1.cs" );
        this.SetGitOutput( "" );

        var writer = this.CreateService();

        this._sync.EnableSyncPoint( BeforeWritingFile( _repository ) );

        var write = QueryAsync( writer, file );
        await this.ReachedAsync( BeforeWritingFile( _repository ) );

        // A second instance stands for another build node: its memory layer is empty and the file is not there yet, so
        // it has to run a command of its own rather than report an answer it does not have. It is observed at the same
        // synchronization point, which is named after the repository rather than after the instance, and which it
        // therefore reaches once its own command has produced a record.
        var second = QueryAsync( this.CreateService(), file );
        await this.ReachedAsync( BeforeWritingFile( _repository ) );

        Assert.Equal( 2, this.ProcessExecutor.StartedProcesses.Count );

        this._sync.DisableSyncPoint( BeforeWritingFile( _repository ) );

        Assert.False( await this.WithTimeout( write ) );
        Assert.False( await this.WithTimeout( second ) );
    }

    /// <summary>
    /// Verifies that two instances storing a record for one repository at the same time leave a record that a third
    /// instance can use. The two writes are not serialized, so what protects the reader is that each write reaches the
    /// file in one operation and that the two contents are equivalent.
    /// </summary>
    [Fact]
    public async Task ConcurrentWritesLeaveAUsableRecord()
    {
        this.CreateRepository( _repository );
        var file = this.CreateSourceFile( _repository, "src/Class1.cs" );
        this.SetGitOutput( "" );

        this._sync.EnableSyncPoint( BeforeWritingFile( _repository ) );

        // Both instances have run their command and are about to write.
        var first = QueryAsync( this.CreateService(), file );
        await this.ReachedAsync( BeforeWritingFile( _repository ) );

        var second = QueryAsync( this.CreateService(), file );
        await this.ReachedAsync( BeforeWritingFile( _repository ) );

        this._sync.DisableSyncPoint( BeforeWritingFile( _repository ) );

        Assert.False( await this.WithTimeout( first ) );
        Assert.False( await this.WithTimeout( second ) );
        Assert.Equal( 2, this.ProcessExecutor.StartedProcesses.Count );

        // A third instance reads what the two of them left and runs no command of its own.
        Assert.False( await this.WithTimeout( QueryAsync( this.CreateService(), file ) ) );
        Assert.Equal( 2, this.ProcessExecutor.StartedProcesses.Count );
    }

    /// <summary>
    /// Verifies that callers sharing one command each derive their own verdict from it. The record describes the
    /// repository, and the two projects here compile different files, only one of which is modified.
    /// </summary>
    [Fact]
    public async Task JoinedCallersDeriveTheirOwnVerdict()
    {
        this.CreateRepository( _repository );
        var modifiedFile = this.CreateSourceFile( _repository, "ProjectA/Class1.cs" );
        var unmodifiedFile = this.CreateSourceFile( _repository, "ProjectB/Class2.cs" );

        this.SetGitOutput( " M ProjectA/Class1.cs\0" );

        var service = this.CreateService();

        this._sync.EnableSyncPoint( InsideCommand( _repository ) );
        this._sync.EnableSyncPoint( JoinedQuery( _repository ) );

        var modified = QueryAsync( service, modifiedFile );
        await this.ReachedAsync( InsideCommand( _repository ) );

        var unmodified = QueryAsync( service, unmodifiedFile );
        await this.ReachedAsync( JoinedQuery( _repository ) );

        this._sync.DisableSyncPoint( JoinedQuery( _repository ) );
        this._sync.DisableSyncPoint( InsideCommand( _repository ) );

        Assert.True( await this.WithTimeout( modified ) );
        Assert.False( await this.WithTimeout( unmodified ) );
        Assert.Single( this.ProcessExecutor.StartedProcesses );
    }

    /// <summary>
    /// Verifies that a failure of the command reaches every caller that shared it, as a verdict of "modified" rather
    /// than as an exception. A build must not fail because git did.
    /// </summary>
    [Fact]
    public async Task JoinedCallersShareAFailedCommand()
    {
        this.CreateRepository( _repository );
        var file = this.CreateSourceFile( _repository, "src/Class1.cs" );

        // A null standard output is how the executor reports a non-zero exit code or an expired timeout.
        this.ProcessExecutor.StandardOutputProvider = _ => null;

        var service = this.CreateService();

        this._sync.EnableSyncPoint( InsideCommand( _repository ) );
        this._sync.EnableSyncPoint( JoinedQuery( _repository ) );

        var first = QueryAsync( service, file );
        await this.ReachedAsync( InsideCommand( _repository ) );

        var second = QueryAsync( service, file );
        await this.ReachedAsync( JoinedQuery( _repository ) );

        this._sync.DisableSyncPoint( JoinedQuery( _repository ) );
        this._sync.DisableSyncPoint( InsideCommand( _repository ) );

        Assert.True( await this.WithTimeout( first ) );
        Assert.True( await this.WithTimeout( second ) );
        Assert.Single( this.ProcessExecutor.StartedProcesses );
    }

    /// <summary>
    /// Verifies that a run is registered only while it is in progress, so that a later query observes the repository
    /// as it is then rather than as it was.
    /// </summary>
    [Fact]
    public async Task AQueryAfterAnotherHasCompletedRunsItsOwnCommand()
    {
        this.CreateRepository( _repository );
        var file = this.CreateSourceFile( _repository, "src/Class1.cs" );
        this.SetGitOutput( "" );

        var service = this.CreateService();

        Assert.False( await this.WithTimeout( QueryAsync( service, file ) ) );

        // The state of the repository changes, which invalidates the stored record.
        this.FileSystem.SetFileLastWriteTime( Path.Combine( _repository, ".git", "index" ), this.Time.UtcNow.ToLocalTime() );
        this.SetGitOutput( " M src/Class1.cs\0" );

        Assert.True( await this.WithTimeout( QueryAsync( service, file ) ) );
        Assert.Equal( 2, this.ProcessExecutor.StartedProcesses.Count );
    }

    /// <summary>
    /// Verifies that the synchronization points cost nothing when no test has enabled them, which is what makes them
    /// acceptable in production code. A query that enables none of them runs to completion without blocking.
    /// </summary>
    [Fact]
    public async Task SynchronizationPointsAreInertWhenNotEnabled()
    {
        this.CreateRepository( _repository );
        var file = this.CreateSourceFile( _repository, "src/Class1.cs" );
        this.SetGitOutput( "" );

        Assert.False( await this.WithTimeout( QueryAsync( this.CreateService(), file ) ) );
        Assert.Single( this.ProcessExecutor.StartedProcesses );
    }

    /// <summary>
    /// Verifies that a caller which gives up while the record is being stored does not take the store down with it.
    /// The record has been produced by a command that has already run, so abandoning the write would throw away work
    /// the build has paid for and leave the next build node to repeat it.
    /// </summary>
    /// <remarks>
    /// The write is what makes this possible: it is performed by the run, which carries no token, rather than by the
    /// caller. A caller that could cancel the write would be cancelling it for every other caller as well, which is
    /// the defect that <see cref="CancellingOneCallerDoesNotCancelTheOthers"/> covers on the waiting side and this
    /// test covers on the storing side.
    /// </remarks>
    [Fact]
    public async Task ACancelledCallerDoesNotAbandonTheStore()
    {
        this.CreateRepository( _repository );
        var file = this.CreateSourceFile( _repository, "src/Class1.cs" );
        this.SetGitOutput( "" );

        var service = this.CreateService();

        using var cancellationTokenSource = new CancellationTokenSource();

        this._sync.EnableSyncPoint( InsideCommand( _repository ) );
        this._sync.EnableSyncPoint( JoinedQuery( _repository ) );
        this._sync.EnableSyncPoint( BeforeWritingFile( _repository ) );

        // The first caller is held inside the command, so that the run is registered before the second caller looks
        // for it. Without that, the second caller can arrive first and start a run of its own.
        var cancelled = QueryAsync( service, file, cancellationTokenSource.Token );
        await this.ReachedAsync( InsideCommand( _repository ) );

        // A second caller joins the run. It is not the subject of the test: it is how the test observes that the
        // write has finished, because the run publishes its result only after the record has been stored. Polling the
        // file instead would make the assertion depend on a duration.
        var joined = QueryAsync( service, file );
        await this.ReachedAsync( JoinedQuery( _repository ) );
        this._sync.DisableSyncPoint( JoinedQuery( _repository ) );

        this._sync.DisableSyncPoint( InsideCommand( _repository ) );
        await this.ReachedAsync( BeforeWritingFile( _repository ) );

        cancellationTokenSource.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>( async () => await this.WithTimeout( cancelled ) );

        this._sync.DisableSyncPoint( BeforeWritingFile( _repository ) );

        Assert.False( await this.WithTimeout( joined ) );

        // A fresh instance stands for another build node: its memory layer is empty, so a hit here can only come from
        // the file, and proves that the record of the cancelled caller was stored in full.
        Assert.False( await this.WithTimeout( QueryAsync( this.CreateService(), file ) ) );
        Assert.Single( this.ProcessExecutor.StartedProcesses );
    }

    /// <summary>
    /// Verifies that a caller cancelled while the file layer is being read is reported as cancelled and leaves the
    /// cache usable. The read belongs to the caller rather than to a shared run, so unlike the write it is cancelled
    /// with the caller, and what has to be shown is that it leaves nothing behind.
    /// </summary>
    [Fact]
    public async Task CancellingWhileReadingTheFileLeavesTheCacheUsable()
    {
        this.CreateRepository( _repository );
        var file = this.CreateSourceFile( _repository, "src/Class1.cs" );
        this.SetGitOutput( "" );

        // A first instance stores the record, so that the file layer has something for the next one to read.
        Assert.False( await this.WithTimeout( QueryAsync( this.CreateService(), file ) ) );

        // A second instance stands for another build node. Its memory layer is empty, so it reaches the file.
        var second = this.CreateService();

        using var cancellationTokenSource = new CancellationTokenSource();

        this._sync.EnableSyncPoint( BeforeReadingFile( _repository ) );

        var cancelled = QueryAsync( second, file, cancellationTokenSource.Token );
        await this.ReachedAsync( BeforeReadingFile( _repository ) );

        cancellationTokenSource.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>( async () => await this.WithTimeout( cancelled ) );

        this._sync.DisableSyncPoint( BeforeReadingFile( _repository ) );

        // The abandoned read stored nothing and spoiled nothing, so the same instance still obtains its verdict from
        // the file rather than from a command of its own.
        Assert.False( await this.WithTimeout( QueryAsync( second, file ) ) );
        Assert.Single( this.ProcessExecutor.StartedProcesses );
    }
}
