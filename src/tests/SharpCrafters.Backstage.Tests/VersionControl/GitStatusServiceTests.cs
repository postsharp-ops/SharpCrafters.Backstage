// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Testing;
using SharpCrafters.Backstage.VersionControl;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace SharpCrafters.Backstage.Tests.VersionControl;

/// <summary>
/// Tests of <see cref="GitStatusService"/>, which decides whether a build is compiling a source tree that nobody has
/// modified and therefore whether licensing applies to it. The <c>git</c> command runs through
/// <c>IProcessExecutor</c>, so these tests need no git installed and are deterministic.
/// </summary>
public sealed class GitStatusServiceTests : TestsBase
{
    private static readonly string _repository = Path.GetFullPath( Path.Combine( Path.GetTempPath(), "repo" ) );
    private static readonly string _otherRepository = Path.GetFullPath( Path.Combine( Path.GetTempPath(), "other" ) );

    public GitStatusServiceTests( ITestOutputHelper logger ) : base( logger ) { }

    private GitStatusService CreateService() => new( this.ServiceProvider );

    /// <summary>
    /// Declares a repository in the mock file system, by creating the <c>.git</c> directory that the service walks up
    /// to find.
    /// </summary>
    private void CreateRepository( string root )
    {
        this.FileSystem.CreateDirectory( Path.Combine( root, ".git" ) );
        this.WriteFile( Path.Combine( root, ".git", "HEAD" ), "ref: refs/heads/main" );
        this.WriteFile( Path.Combine( root, ".git", "index" ), "index" );
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
    /// Declares a source file of a repository, written in the past so that it does not fall inside the margin that
    /// invalidates a cached record.
    /// </summary>
    private string CreateSourceFile( string root, string relativePath )
    {
        var path = this.WriteFile( Path.Combine( root, relativePath.Replace( '/', Path.DirectorySeparatorChar ) ) );
        this.FileSystem.SetFileLastWriteTime( path, this.Time.UtcNow.AddHours( -1 ).ToLocalTime() );

        return path;
    }

    /// <summary>
    /// Builds the output of <c>git status --porcelain=v1 -z</c> from entries given as they appear on the wire, that
    /// is with the two status columns, a space and the path, and with a NUL after every record.
    /// </summary>
    private static string Porcelain( params string[] entries ) => string.Concat( entries.Select( e => e + "\0" ) );

    private void SetGitOutput( string output ) => this.ProcessExecutor.StandardOutputProvider = _ => output;

    /// <summary>
    /// Verifies that a source tree in which git reports nothing is not modified, which is the case in which the
    /// caller is entitled to skip licensing.
    /// </summary>
    [Fact]
    public async Task CleanRepositoryIsNotModified()
    {
        this.CreateRepository( _repository );
        var file = this.CreateSourceFile( _repository, "src/Class1.cs" );
        this.SetGitOutput( "" );

        Assert.False( await this.CreateService().IsAnyFileModifiedAsync( [file] ) );
    }

    /// <summary>
    /// Verifies that an unstaged modification of a compiled file is reported, which is the ordinary case of a
    /// developer at work.
    /// </summary>
    [Fact]
    public async Task UnstagedModificationIsModified()
    {
        this.CreateRepository( _repository );
        var file = this.CreateSourceFile( _repository, "src/Class1.cs" );
        this.SetGitOutput( Porcelain( " M src/Class1.cs" ) );

        Assert.True( await this.CreateService().IsAnyFileModifiedAsync( [file] ) );
    }

    /// <summary>
    /// Verifies that a modification that has been staged is reported. Staging changes where the modification is
    /// recorded, not whether the user made it, so a check that counted only the unstaged form would be waived by
    /// staging an edit.
    /// </summary>
    [Theory]
    [InlineData( "M  src/Class1.cs" )]
    [InlineData( "MM src/Class1.cs" )]
    public async Task StagedModificationIsModified( string entry )
    {
        this.CreateRepository( _repository );
        var file = this.CreateSourceFile( _repository, "src/Class1.cs" );
        this.SetGitOutput( Porcelain( entry ) );

        Assert.True( await this.CreateService().IsAnyFileModifiedAsync( [file] ) );
    }

    /// <summary>
    /// Verifies that the statuses that are not a content modification of a tracked file do not count. A build writes
    /// generated sources and a restore writes package sources, and neither is the work of the user; a rule that
    /// counted them would report every source tree as modified and make the feature useless.
    /// </summary>
    [Theory]
    [InlineData( "?? src/Class1.cs" )]
    [InlineData( "A  src/Class1.cs" )]
    [InlineData( "D  src/Class1.cs" )]
    [InlineData( " D src/Class1.cs" )]
    public async Task StatusesOtherThanModificationAreIgnored( string entry )
    {
        this.CreateRepository( _repository );
        var file = this.CreateSourceFile( _repository, "src/Class1.cs" );
        this.SetGitOutput( Porcelain( entry ) );

        Assert.False( await this.CreateService().IsAnyFileModifiedAsync( [file] ) );
    }

    /// <summary>
    /// Verifies that a file modified in the repository but absent from the compilation does not make the project
    /// modified. The question asked is about the files of the project, not about the repository.
    /// </summary>
    [Fact]
    public async Task ModificationOutsideTheCompiledSetIsIgnored()
    {
        this.CreateRepository( _repository );
        var file = this.CreateSourceFile( _repository, "src/Class1.cs" );
        this.CreateSourceFile( _repository, "README.md" );
        this.SetGitOutput( Porcelain( " M README.md" ) );

        Assert.False( await this.CreateService().IsAnyFileModifiedAsync( [file] ) );
    }

    /// <summary>
    /// Verifies that a rename, which emits two records instead of one under <c>-z</c>, does not desynchronise the
    /// parser. A parser that failed to consume the second record would read the old path as the status of the next
    /// entry and misread everything that follows, including the modification that this test places after it.
    /// </summary>
    [Fact]
    public async Task RenameDoesNotDesynchroniseTheParser()
    {
        this.CreateRepository( _repository );
        var renamed = this.CreateSourceFile( _repository, "src/New.cs" );
        var modified = this.CreateSourceFile( _repository, "src/Class1.cs" );

        this.SetGitOutput( Porcelain( "R  src/New.cs", "src/Old.cs", " M src/Class1.cs" ) );

        var service = this.CreateService();

        // The rename itself does not count.
        Assert.False( await service.IsAnyFileModifiedAsync( [renamed] ) );

        // The entry after the rename is still read correctly.
        Assert.True( await service.IsAnyFileModifiedAsync( [modified] ) );
    }

    /// <summary>
    /// Verifies that a path outside ASCII is matched. This is what the <c>-z</c> option buys: without it git would
    /// quote the path and the comparison would fail, which would waive the check.
    /// </summary>
    [Fact]
    public async Task NonAsciiPathIsMatched()
    {
        this.CreateRepository( _repository );
        var file = this.CreateSourceFile( _repository, "src/Été.cs" );
        this.SetGitOutput( Porcelain( " M src/Été.cs" ) );

        Assert.True( await this.CreateService().IsAnyFileModifiedAsync( [file] ) );
    }

    /// <summary>
    /// Verifies that a source tree that is in no repository is reported as modified. Nothing is known about it, and
    /// an unknown status must never waive licensing.
    /// </summary>
    [Fact]
    public async Task FilesWithoutRepositoryAreModified()
    {
        var file = this.WriteFile( Path.Combine( Path.GetTempPath(), "loose", "Class1.cs" ) );

        Assert.True( await this.CreateService().IsAnyFileModifiedAsync( [file] ) );
        Assert.Empty( this.ProcessExecutor.StartedProcesses );
    }

    /// <summary>
    /// Verifies that a file outside any repository does not, by itself, make a project modified when other files do
    /// belong to one. This is an accepted limit: a file the repository does not track is one the user does not
    /// control.
    /// </summary>
    [Fact]
    public async Task FilesWithoutRepositoryAreIgnoredWhenOthersHaveOne()
    {
        this.CreateRepository( _repository );
        var tracked = this.CreateSourceFile( _repository, "src/Class1.cs" );
        var loose = this.WriteFile( Path.Combine( Path.GetTempPath(), "loose", "Generated.g.cs" ) );
        this.SetGitOutput( "" );

        Assert.False( await this.CreateService().IsAnyFileModifiedAsync( [tracked, loose] ) );
    }

    /// <summary>
    /// Verifies that a machine without git builds normally, with licensing enforced. Starting a process that does not
    /// exist throws rather than failing, so this is the case the <c>catch</c> around the call exists for.
    /// </summary>
    [Fact]
    public async Task MissingGitIsModified()
    {
        this.CreateRepository( _repository );
        var file = this.CreateSourceFile( _repository, "src/Class1.cs" );
        this.ProcessExecutor.ExceptionToThrow = new Win32Exception( 2, "The system cannot find the file specified." );

        Assert.True( await this.CreateService().IsAnyFileModifiedAsync( [file] ) );
    }

    /// <summary>
    /// Verifies that a command that fails, for instance because the repository is owned by another account, leaves
    /// licensing enforced.
    /// </summary>
    [Fact]
    public async Task FailedCommandIsModified()
    {
        this.CreateRepository( _repository );
        var file = this.CreateSourceFile( _repository, "src/Class1.cs" );

        // A null standard output is how the executor reports a non-zero exit code or an expired timeout.
        this.ProcessExecutor.StandardOutputProvider = _ => null;

        Assert.True( await this.CreateService().IsAnyFileModifiedAsync( [file] ) );
    }

    /// <summary>
    /// Verifies that the command is the one this feature depends on. Each option is load-bearing, and the reasons are
    /// in docs/vcs-check.md, so the arguments are pinned rather than described.
    /// </summary>
    [Fact]
    public async Task CommandIsPinned()
    {
        this.CreateRepository( _repository );
        var file = this.CreateSourceFile( _repository, "src/Class1.cs" );
        this.SetGitOutput( "" );

        await this.CreateService().IsAnyFileModifiedAsync( [file] );

        var startInfo = Assert.Single( this.ProcessExecutor.StartedProcesses );
        Assert.Equal( "git", startInfo.FileName );

        Assert.Equal(
            "--no-optional-locks status --porcelain=v1 -z --untracked-files=no --ignore-submodules=all",
            startInfo.Arguments );

        Assert.Equal( _repository, startInfo.WorkingDirectory );
    }

    /// <summary>
    /// Verifies that the git command can be named by an environment variable, for a machine on which git is installed
    /// but is not on the search path. Without it such a machine reports every source tree as modified, which is
    /// correct but unhelpful when the user knows where git is.
    /// </summary>
    [Fact]
    public async Task TheGitCommandCanBeNamedByAnEnvironmentVariable()
    {
        this.CreateRepository( _repository );
        var file = this.CreateSourceFile( _repository, "src/Class1.cs" );
        this.SetGitOutput( "" );

        this.EnvironmentVariableProvider.Environment["METALAMA_GIT_PATH"] = "/opt/git/bin/git";

        Assert.False( await this.CreateService().IsAnyFileModifiedAsync( [file] ) );

        var startInfo = Assert.Single( this.ProcessExecutor.StartedProcesses );
        Assert.Equal( "/opt/git/bin/git", startInfo.FileName );
    }

    /// <summary>
    /// Verifies that the command is the one on the search path when the environment names none, which is the ordinary
    /// case.
    /// </summary>
    [Fact]
    public async Task TheGitCommandDefaultsToTheSearchPath()
    {
        this.CreateRepository( _repository );
        var file = this.CreateSourceFile( _repository, "src/Class1.cs" );
        this.SetGitOutput( "" );

        Assert.False( await this.CreateService().IsAnyFileModifiedAsync( [file] ) );

        Assert.Equal( "git", Assert.Single( this.ProcessExecutor.StartedProcesses ).FileName );
    }

    /// <summary>
    /// Verifies that the variables through which the environment can point git at another repository are removed, and
    /// not merely emptied. A build started from a git hook inherits them, and git would otherwise report on a
    /// temporary index instead of the repository. Git distinguishes a variable set to nothing from one that is not
    /// set, so emptying them would make it refuse to run.
    /// </summary>
    [Fact]
    public async Task GitEnvironmentIsRemoved()
    {
        this.CreateRepository( _repository );
        var file = this.CreateSourceFile( _repository, "src/Class1.cs" );
        this.SetGitOutput( "" );

        await this.CreateService().IsAnyFileModifiedAsync( [file] );

        var startInfo = Assert.Single( this.ProcessExecutor.StartedProcesses );

        foreach ( var variable in new[] { "GIT_DIR", "GIT_WORK_TREE", "GIT_INDEX_FILE" } )
        {
            Assert.False( startInfo.Environment.ContainsKey( variable ), $"'{variable}' is still in the environment." );
        }
    }

    /// <summary>
    /// Verifies that a repository whose <c>.git</c> is a file rather than a directory is found. That is the layout of
    /// a submodule and of a linked working tree.
    /// </summary>
    [Fact]
    public async Task RepositoryWithGitFileIsFound()
    {
        var root = Path.Combine( Path.GetTempPath(), "worktree" );
        this.WriteFile( Path.Combine( root, ".git" ), "gitdir: /elsewhere/.git/worktrees/x" );
        var file = this.CreateSourceFile( root, "src/Class1.cs" );
        this.SetGitOutput( Porcelain( " M src/Class1.cs" ) );

        Assert.True( await this.CreateService().IsAnyFileModifiedAsync( [file] ) );
    }

    /// <summary>
    /// Verifies that the files of several repositories are each tested against their own repository.
    /// </summary>
    [Fact]
    public async Task EachRepositoryIsQueriedSeparately()
    {
        this.CreateRepository( _repository );
        this.CreateRepository( _otherRepository );
        var first = this.CreateSourceFile( _repository, "src/Class1.cs" );
        var second = this.CreateSourceFile( _otherRepository, "src/Class2.cs" );

        this.ProcessExecutor.StandardOutputProvider =
            startInfo => startInfo.WorkingDirectory == _otherRepository ? Porcelain( " M src/Class2.cs" ) : "";

        Assert.True( await this.CreateService().IsAnyFileModifiedAsync( [first, second] ) );
        Assert.Equal( 2, this.ProcessExecutor.StartedProcesses.Count );
    }

    /// <summary>
    /// Verifies that a cancellation during the command is reported to the caller rather than being turned into a
    /// verdict. A query that was abandoned has no answer, and answering "modified" would enforce licensing on a build
    /// that the user has stopped.
    /// </summary>
    [Fact]
    public async Task CancellationIsReported()
    {
        this.CreateRepository( _repository );
        var file = this.CreateSourceFile( _repository, "src/Class1.cs" );

        using var cancellationTokenSource = new CancellationTokenSource();

        this.ProcessExecutor.AsyncStandardOutputProvider = async ( _, cancellationToken ) =>
        {
            cancellationTokenSource.Cancel();
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();

            return "";
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await this.CreateService().IsAnyFileModifiedAsync( [file], cancellationTokenSource.Token ) );
    }

    /// <summary>
    /// Verifies that a path given as relative is refused rather than resolved against whatever the current directory
    /// of the process happens to be, which a build host changes from one project to the next.
    /// </summary>
    [Fact]
    public async Task UnknownPathsDoNotWaiveTheCheck()
    {
        Assert.True( await this.CreateService().IsAnyFileModifiedAsync( [""] ) );
    }

    /// <summary>
    /// Verifies that a repository holding a path which is not legal on the current operating system leaves licensing
    /// enforced rather than failing the build. A file named <c>a:b.cs</c> is legal on Unix, and
    /// <c>Path.Combine</c> rejects it on .NET Framework, which is the runtime of the licensing task under
    /// Visual Studio.
    /// </summary>
    [Fact]
    public async Task AnIllegalPathInTheRepositoryIsModified()
    {
        this.CreateRepository( _repository );
        var file = this.CreateSourceFile( _repository, "src/Class1.cs" );
        this.SetGitOutput( Porcelain( " M src/a:b|c.cs", " M src/Class1.cs" ) );

        // On the runtimes where Path.Combine accepts the path, the file simply does not match and the status is read
        // normally; on the others the repository reads as unknown. Either way licensing is enforced, which is the
        // promise being protected.
        Assert.True( await this.CreateService().IsAnyFileModifiedAsync( [file] ) );
    }

    /// <summary>
    /// Verifies that the parser reads the wire format of <c>--porcelain=v1 -z</c>, including the two records that a
    /// rename emits and the trailing terminator of the last record.
    /// </summary>
    [Fact]
    public void ParserReadsThePorcelainFormat()
    {
        var output = Porcelain( "R  new.cs", "old.cs", " M a.cs", "?? b.cs", "MM c.cs" );

        var modified = GitStatusService.ParseStatus( output, _repository );

        Assert.Equal(
            new List<string> { Path.Combine( _repository, "a.cs" ), Path.Combine( _repository, "c.cs" ) },
            modified );
    }
}
