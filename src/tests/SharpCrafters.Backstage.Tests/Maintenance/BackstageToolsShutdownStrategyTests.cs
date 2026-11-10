// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Application;
using SharpCrafters.Backstage.Extensibility;
using SharpCrafters.Backstage.Maintenance;
using SharpCrafters.Backstage.ProcessClassification;
using SharpCrafters.Backstage.Testing;
using SharpCrafters.Backstage.Tools;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Xunit;

namespace SharpCrafters.Backstage.Tests.Maintenance;

public sealed class BackstageToolsShutdownStrategyTests : TestsBase
{
    public BackstageToolsShutdownStrategyTests( ITestOutputHelper logger ) : base( logger ) { }

    protected override void ConfigureServices( ServiceProviderBuilder services )
    {
        base.ConfigureServices( services );

        // The real process manager: the test starts a real process, which is what the strategy has to find. The parents of
        // the test process do not matter here, so none is reported.
        services.AddService( typeof(IParentProcessSearch), _ => new NoParentProcessSearch() );
        services.AddService( typeof(IProcessManager), serviceProvider => new WindowsProcessManager( serviceProvider ) );
    }

    private sealed class NoParentProcessSearch : IParentProcessSearch
    {
        public IReadOnlyList<ProcessInfo> GetParentProcesses( ISet<string>? pivots = null ) => [];
    }

    /// <summary>
    /// The name of the machine-wide mutex that makes the tests of this class mutually exclusive across processes.
    /// </summary>
    /// <remarks>
    /// The strategy ends every process that has the name of a tool. The test assembly targets several frameworks, and
    /// <c>dotnet test</c> runs them in parallel, each in its own process. Without the mutex, the strategy of one test run
    /// ends the process that the other test run started, and the other test run then finds no process.
    /// </remarks>
    private const string _mutexName = @"Global\SharpCrafters.Backstage.Tests.BackstageToolsShutdownStrategyTests";

    private static readonly TimeSpan _mutexTimeout = TimeSpan.FromMinutes( 2 );

    /// <summary>
    /// Acquires the mutex named <see cref="_mutexName"/>. The caller releases it by disposing the result, on the same thread.
    /// </summary>
    private static IDisposable AcquireMutex()
    {
        var mutex = new Mutex( false, _mutexName );

        try
        {
            bool acquired;

            try
            {
                acquired = mutex.WaitOne( _mutexTimeout );
            }
            catch ( AbandonedMutexException )
            {
                // A test process exited while it owned the mutex. The current thread owns it now.
                acquired = true;
            }

            if ( !acquired )
            {
                throw new TimeoutException( $"The mutex '{_mutexName}' was not acquired within {_mutexTimeout}." );
            }

            return new MutexReleaser( mutex );
        }
        catch
        {
            mutex.Dispose();

            throw;
        }
    }

    private sealed class MutexReleaser : IDisposable
    {
        private readonly Mutex _mutex;

        public MutexReleaser( Mutex mutex )
        {
            this._mutex = mutex;
        }

        public void Dispose()
        {
            this._mutex.ReleaseMutex();
            this._mutex.Dispose();
        }
    }

    /// <summary>
    /// Tests that a running desktop notifier is ended and reported, even without <c>--force</c>. The <c>shutdown</c> command
    /// ends the tools before files are replaced, and a notifier that is not ended keeps them locked.
    /// </summary>
    /// <remarks>
    /// The notifier is impersonated by a copy of <c>cmd.exe</c> named after it, which waits for a key on a console without a
    /// window.
    /// It runs on Windows only, where the notifier exists.
    /// </remarks>
    [PlatformFact( TestPlatforms.Windows )]
    public void ARunningNotifierIsShutDown()
    {
        using var mutex = AcquireMutex();

        var profile = this.ServiceProvider.GetRequiredBackstageService<ProductProfile>();
        var directory = Path.Combine( Path.GetTempPath(), "ToolProcessesTests", Guid.NewGuid().ToString( "N" ) );
        Directory.CreateDirectory( directory );

        var executable = Path.Combine( directory, BackstageTool.DesktopWindows.GetAssemblyName( profile ) + ".exe" );
        File.Copy( Path.Combine( Environment.SystemDirectory, "cmd.exe" ), executable );

        // The standard input is not redirected. On .NET Framework, the writer of a redirected standard input writes the
        // preamble of the console input encoding as soon as it is created. With the UTF-8 code page, 'pause' reads it as a
        // key, and the process exits before the strategy looks for it. Without redirection, the process gets a console of
        // its own, without a window, and 'pause' waits on it.
        using var process = Process.Start( new ProcessStartInfo( executable, "/d /c pause" ) { UseShellExecute = false, CreateNoWindow = true } )!;

        try
        {
            var results = new BackstageToolsShutdownStrategy( this.ServiceProvider )
                .ShutDownProcesses( new ProcessShutdownOptions( false, TimeSpan.Zero ) );

            var result = Assert.Single( results, r => r.ProcessId == process.Id );
            Assert.Equal( ProcessShutdownOutcome.Ended, result.Outcome );
            Assert.Equal( $"{profile.Name} notifier", result.Description );
            Assert.Null( result.Reason );
            Assert.True( process.HasExited );
        }
        finally
        {
            if ( !process.HasExited )
            {
                process.Kill();
            }

            process.WaitForExit();

            try
            {
                Directory.Delete( directory, true );
            }
            catch ( IOException )
            {
                // The executable may be locked for a moment after the process exited. It is in the temporary directory.
            }
        }
    }
}
