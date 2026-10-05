// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Extensibility;
using SharpCrafters.Backstage.Infrastructure;
using SharpCrafters.Backstage.Telemetry;
using SharpCrafters.Backstage.Testing;
using SharpCrafters.Backstage.Threading;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace SharpCrafters.Backstage.Tests.Telemetry;

/// <summary>
/// Tests the store that keeps the time of the last usage report of each project in a file of its own (issue 2092).
/// </summary>
public sealed class FileUsageSessionStoreTests : TestsBase
{
    private static readonly DateTime _now = new( 2026, 9, 15, 10, 30, 0, DateTimeKind.Utc );
    private static readonly TimeSpan _period = TimeSpan.FromDays( 1 );

    public FileUsageSessionStoreTests( ITestOutputHelper logger ) : base( logger ) { }

    private FileUsageSessionStore CreateStore() => new( this.ServiceProvider );

    private string SessionsDirectory => this.ServiceProvider.GetRequiredBackstageService<IStandardDirectories>().TelemetrySessionsDirectory;

    [Fact]
    public void AProjectIsClaimedOncePerPeriod()
    {
        var store = this.CreateStore();

        Assert.True( store.TryClaim( "Project", _now, _period ) );
        Assert.False( store.TryClaim( "Project", _now, _period ) );
        Assert.False( store.TryClaim( "Project", _now + _period - TimeSpan.FromMinutes( 1 ), _period ) );
        Assert.True( store.TryClaim( "Project", _now + _period, _period ) );
    }

    [Fact]
    public void TheRecordIsSharedByEveryInstance()
    {
        Assert.True( this.CreateStore().TryClaim( "Project", _now, _period ) );
        Assert.False( this.CreateStore().TryClaim( "Project", _now, _period ) );
    }

    [Fact]
    public void TheProjectKeyIsComparedWithoutCase()
    {
        var store = this.CreateStore();

        Assert.True( store.TryClaim( "MyProject", _now, _period ) );
        Assert.False( store.TryClaim( "MYPROJECT", _now, _period ) );
        Assert.Single( this.FileSystem.GetFiles( this.SessionsDirectory ) );
    }

    [Fact]
    public void EachProjectHasAFileOfItsOwn()
    {
        var store = this.CreateStore();

        Assert.True( store.TryClaim( "Project1", _now, _period ) );
        Assert.True( store.TryClaim( "Project2", _now, _period ) );

        Assert.Equal( 2, this.FileSystem.GetFiles( this.SessionsDirectory ).Length );
    }

    [Theory]
    [InlineData( "<TestSession>" )]
    [InlineData( @"C:\src\a project\a.csproj" )]
    [InlineData( "a|b?c*d:e\"f" )]
    public void AProjectKeyThatIsNotAValidFileNameIsAccepted( string projectKey )
    {
        var store = this.CreateStore();

        Assert.True( store.TryClaim( projectKey, _now, _period ) );
        Assert.False( store.TryClaim( projectKey, _now, _period ) );

        var file = Assert.Single( this.FileSystem.GetFiles( this.SessionsDirectory ) );
        Assert.Contains( projectKey, this.FileSystem.ReadAllText( file ), StringComparison.Ordinal );
    }

    [Fact]
    public void ACorruptedRecordCountsAsNoRecord()
    {
        var store = this.CreateStore();
        var path = store.GetFilePath( "Project" );

        this.FileSystem.CreateDirectory( Path.GetDirectoryName( path )! );
        this.FileSystem.WriteAllText( path, "not a date" );

        Assert.True( store.TryClaim( "Project", _now, _period ) );
        Assert.False( store.TryClaim( "Project", _now, _period ) );
    }

    [Fact]
    public void AProjectThatAnotherProcessIsClaimingIsNotClaimed()
    {
        var store = this.CreateStore();

        using ( this.Locks.Pin( this.Locks.GetGlobalLockName( store.GetFilePath( "Project" ) ) ) )
        {
            // The lock is not waited for: its holder is about to record the same project.
            Assert.False( store.TryClaim( "Project", _now, _period ) );
        }

        Assert.True( store.TryClaim( "Project", _now, _period ) );
    }

    [Fact]
    public void ALockHeldForAnotherProjectDoesNotPreventAClaim()
    {
        var store = this.CreateStore();

        using ( this.Locks.Pin( this.Locks.GetGlobalLockName( store.GetFilePath( "Project1" ) ) ) )
        {
            Assert.True( store.TryClaim( "Project2", _now, _period ) );
        }
    }

    [Fact]
    public async Task ConcurrentClaimsOfOneProjectSucceedOnceAsync()
    {
        var store = this.CreateStore();
        using var start = new ManualResetEventSlim();

        var tasks = Enumerable.Range( 0, 16 )
            .Select(
                _ => Task.Run(
                    () =>
                    {
                        start.Wait( TestContext.Current.CancellationToken );

                        return store.TryClaim( "Project", _now, _period );
                    },
                    TestContext.Current.CancellationToken ) )
            .ToArray();

        start.Set();

        var results = await Task.WhenAll( tasks );

        Assert.Single( results, r => r );
    }

    [Fact]
    public async Task ConcurrentClaimsOfDistinctProjectsAllSucceedAsync()
    {
        var store = this.CreateStore();
        using var start = new ManualResetEventSlim();

        var tasks = Enumerable.Range( 0, 16 )
            .Select(
                i => Task.Run(
                    () =>
                    {
                        start.Wait( TestContext.Current.CancellationToken );

                        return store.TryClaim( "Project" + i, _now, _period );
                    },
                    TestContext.Current.CancellationToken ) )
            .ToArray();

        start.Set();

        var results = await Task.WhenAll( tasks );

        Assert.All( results, Assert.True );
    }
}
