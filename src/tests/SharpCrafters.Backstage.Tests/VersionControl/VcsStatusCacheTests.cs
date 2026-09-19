// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Testing;
using SharpCrafters.Backstage.VersionControl;
using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace SharpCrafters.Backstage.Tests.VersionControl;

/// <summary>
/// Tests of the two-layer cache of <see cref="GitStatusService"/>. What it protects is the cost of the check: the
/// repository is queried once per build rather than once per project. What it must never do is report a stale
/// "unmodified", because that waives licensing.
/// </summary>
public sealed class VcsStatusCacheTests : TestsBase
{
    private static readonly string _repository = Path.GetFullPath( Path.Combine( Path.GetTempPath(), "repo" ) );

    public VcsStatusCacheTests( ITestOutputHelper logger ) : base( logger ) { }

    private GitStatusService CreateService() => new( this.ServiceProvider );

    private void CreateRepository()
    {
        this.FileSystem.CreateDirectory( Path.Combine( _repository, ".git" ) );
        this.WriteInThePast( Path.Combine( _repository, ".git", "HEAD" ), "ref: refs/heads/main" );
        this.WriteInThePast( Path.Combine( _repository, ".git", "index" ), "index" );
    }

    /// <summary>
    /// Writes a file and dates it an hour ago, so that it falls outside the margin that invalidates a record.
    /// </summary>
    private string WriteInThePast( string path, string content = "content" )
    {
        var directory = Path.GetDirectoryName( path )!;

        if ( !this.FileSystem.DirectoryExists( directory ) )
        {
            this.FileSystem.CreateDirectory( directory );
        }

        this.FileSystem.WriteAllText( path, content );
        this.FileSystem.SetFileLastWriteTime( path, this.Time.UtcNow.AddHours( -1 ).ToLocalTime() );

        return path;
    }

    private string CreateSourceFile( string relativePath )
        => this.WriteInThePast( Path.Combine( _repository, relativePath.Replace( '/', Path.DirectorySeparatorChar ) ) );

    private void SetGitOutput( string output ) => this.ProcessExecutor.StandardOutputProvider = _ => output;

    /// <summary>
    /// Verifies that a second query of the same repository within one process runs no command. This is the layer that
    /// spares the projects that one build node compiles after the first.
    /// </summary>
    [Fact]
    public async Task SecondQueryInTheSameProcessRunsNoCommand()
    {
        this.CreateRepository();
        var file = this.CreateSourceFile( "src/Class1.cs" );
        this.SetGitOutput( "" );

        var service = this.CreateService();

        Assert.False( await service.IsAnyFileModifiedAsync( [file] ) );
        Assert.False( await service.IsAnyFileModifiedAsync( [file] ) );

        Assert.Single( this.ProcessExecutor.StartedProcesses );
    }

    /// <summary>
    /// Verifies that two projects of one repository, which compile disjoint sets of files, share one command. This is
    /// the reason the record holds the answer of git about the repository rather than the verdict about a project: a
    /// record holding the queried file list would be invalidated by the next project, and a solution of fifty
    /// projects would run fifty commands.
    /// </summary>
    [Fact]
    public async Task TwoProjectsOfOneRepositoryShareOneCommand()
    {
        this.CreateRepository();
        var first = this.CreateSourceFile( "ProjectA/Class1.cs" );
        var second = this.CreateSourceFile( "ProjectB/Class2.cs" );
        this.SetGitOutput( "" );

        var service = this.CreateService();

        Assert.False( await service.IsAnyFileModifiedAsync( [first] ) );
        Assert.False( await service.IsAnyFileModifiedAsync( [second] ) );

        Assert.Single( this.ProcessExecutor.StartedProcesses );
    }

    /// <summary>
    /// Verifies that a second process reuses the record that the first one stored, which is the layer that spares the
    /// other build nodes and the subsequent builds.
    /// </summary>
    [Fact]
    public async Task SecondProcessReusesTheStoredRecord()
    {
        this.CreateRepository();
        var file = this.CreateSourceFile( "src/Class1.cs" );
        this.SetGitOutput( "" );

        Assert.False( await this.CreateService().IsAnyFileModifiedAsync( [file] ) );

        // A new instance has an empty memory layer and stands for another build node.
        Assert.False( await this.CreateService().IsAnyFileModifiedAsync( [file] ) );

        Assert.Single( this.ProcessExecutor.StartedProcesses );
    }

    /// <summary>
    /// Verifies that editing a source file invalidates the record. Without this, a developer who edits a file after a
    /// clean build would keep the exemption that the clean build earned.
    /// </summary>
    [Fact]
    public async Task WritingASourceFileInvalidatesTheRecord()
    {
        this.CreateRepository();
        var file = this.CreateSourceFile( "src/Class1.cs" );
        this.SetGitOutput( "" );

        Assert.False( await this.CreateService().IsAnyFileModifiedAsync( [file] ) );

        this.FileSystem.SetFileLastWriteTime( file, this.Time.UtcNow.ToLocalTime() );
        this.SetGitOutput( Porcelain( " M src/Class1.cs" ) );

        Assert.True( await this.CreateService().IsAnyFileModifiedAsync( [file] ) );
        Assert.Equal( 2, this.ProcessExecutor.StartedProcesses.Count );
    }

    /// <summary>
    /// Verifies that a change of the git state invalidates the record even though no source file was written. A
    /// branch switch, a reset, a stash or a staging operation changes the answer without touching any source file, so
    /// the modification times of the source files alone would not notice it.
    /// </summary>
    [Theory]
    [InlineData( "index" )]
    [InlineData( "HEAD" )]
    public async Task ChangingTheGitStateInvalidatesTheRecord( string gitFile )
    {
        this.CreateRepository();
        var file = this.CreateSourceFile( "src/Class1.cs" );
        this.SetGitOutput( "" );

        Assert.False( await this.CreateService().IsAnyFileModifiedAsync( [file] ) );

        this.FileSystem.SetFileLastWriteTime( Path.Combine( _repository, ".git", gitFile ), this.Time.UtcNow.ToLocalTime() );
        this.SetGitOutput( Porcelain( "M  src/Class1.cs" ) );

        Assert.True( await this.CreateService().IsAnyFileModifiedAsync( [file] ) );
        Assert.Equal( 2, this.ProcessExecutor.StartedProcesses.Count );
    }

    /// <summary>
    /// Verifies that a file written in the two seconds before the query started does not read as covered by the
    /// record. That interval is the one in which the command was starting, and it is also the granularity of the
    /// modification times of FAT, exFAT and SMB volumes.
    /// </summary>
    [Fact]
    public async Task AFileWrittenWithinTheMarginInvalidatesTheRecord()
    {
        this.CreateRepository();
        var file = this.CreateSourceFile( "src/Class1.cs" );
        this.SetGitOutput( "" );

        Assert.False( await this.CreateService().IsAnyFileModifiedAsync( [file] ) );

        this.FileSystem.SetFileLastWriteTime( file, this.Time.UtcNow.AddSeconds( -1 ).ToLocalTime() );

        Assert.False( await this.CreateService().IsAnyFileModifiedAsync( [file] ) );
        Assert.Equal( 2, this.ProcessExecutor.StartedProcesses.Count );
    }

    /// <summary>
    /// Verifies that the record produced by the command is used even though the build has just written one of the
    /// files it compiles, which is what a build does to the sources it generates. The staleness rule applies to a
    /// record read from a cache, never to one the command has just produced: the latter is authoritative by
    /// construction, and submitting it to the rule would make the feature never reach a verdict at all.
    /// </summary>
    [Fact]
    public async Task AFreshRecordIsNotSubjectToTheStalenessRule()
    {
        this.CreateRepository();
        var generated = this.CreateSourceFile( "obj/Assembly.g.cs" );
        this.FileSystem.SetFileLastWriteTime( generated, this.Time.UtcNow.ToLocalTime() );
        this.SetGitOutput( "" );

        Assert.False( await this.CreateService().IsAnyFileModifiedAsync( [generated] ) );
        Assert.Single( this.ProcessExecutor.StartedProcesses );
    }

    private static string Porcelain( params string[] entries ) => string.Concat( Array.ConvertAll( entries, e => e + "\0" ) );
}
