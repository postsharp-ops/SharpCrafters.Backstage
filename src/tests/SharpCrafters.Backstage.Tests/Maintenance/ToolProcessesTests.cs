// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Application;
using SharpCrafters.Backstage.Extensibility;
using SharpCrafters.Backstage.Maintenance;
using SharpCrafters.Backstage.Testing;
using SharpCrafters.Backstage.Tools;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Xunit;
using Xunit.Abstractions;

namespace SharpCrafters.Backstage.Tests.Maintenance;

public sealed class ToolProcessesTests : TestsBase
{
    public ToolProcessesTests( ITestOutputHelper logger ) : base( logger ) { }

    /// <summary>
    /// Tests that a running desktop notifier is found. A product stops its tools before files are replaced, for instance
    /// by a <c>shutdown</c> command, and a notifier that is not found keeps them locked and is left behind.
    /// </summary>
    /// <remarks>
    /// The notifier is impersonated by a copy of <c>cmd.exe</c> named after it, which waits for input that never comes.
    /// It runs on Windows only, where the notifier exists.
    /// </remarks>
    [SkippableFact]
    public void ARunningNotifierIsFound()
    {
        Skip.IfNot( RuntimeInformation.IsOSPlatform( OSPlatform.Windows ), "The desktop notifier exists on Windows only." );

        var profile = this.ServiceProvider.GetRequiredBackstageService<ProductProfile>();
        var directory = Path.Combine( Path.GetTempPath(), "ToolProcessesTests", Guid.NewGuid().ToString( "N" ) );
        Directory.CreateDirectory( directory );

        var executable = Path.Combine( directory, BackstageTool.DesktopWindows.GetAssemblyName( profile ) + ".exe" );
        File.Copy( Path.Combine( Environment.SystemDirectory, "cmd.exe" ), executable );

        using var process = Process.Start(
            new ProcessStartInfo( executable, "/d /c pause" ) { UseShellExecute = false, RedirectStandardInput = true, CreateNoWindow = true } )!;

        try
        {
            var toolProcesses = new WindowsProcessManager( this.ServiceProvider ).GetToolProcesses();

            var found = Assert.Single( toolProcesses, p => p.Process.Id == process.Id );
            Assert.Same( BackstageTool.DesktopWindows, found.Tool );
        }
        finally
        {
            process.Kill();
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
