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
using Xunit;
using Xunit.Abstractions;

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
    /// Tests that a running desktop notifier is ended and reported, even without <c>--force</c>. The <c>shutdown</c> command
    /// ends the tools before files are replaced, and a notifier that is not ended keeps them locked.
    /// </summary>
    /// <remarks>
    /// The notifier is impersonated by a copy of <c>cmd.exe</c> named after it, which waits for input that never comes.
    /// It runs on Windows only, where the notifier exists.
    /// </remarks>
    [PlatformFact( TestPlatforms.Windows )]
    public void ARunningNotifierIsShutDown()
    {
        var profile = this.ServiceProvider.GetRequiredBackstageService<ProductProfile>();
        var directory = Path.Combine( Path.GetTempPath(), "ToolProcessesTests", Guid.NewGuid().ToString( "N" ) );
        Directory.CreateDirectory( directory );

        var executable = Path.Combine( directory, BackstageTool.DesktopWindows.GetAssemblyName( profile ) + ".exe" );
        File.Copy( Path.Combine( Environment.SystemDirectory, "cmd.exe" ), executable );

        using var process = Process.Start(
            new ProcessStartInfo( executable, "/d /c pause" ) { UseShellExecute = false, RedirectStandardInput = true, CreateNoWindow = true } )!;

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
