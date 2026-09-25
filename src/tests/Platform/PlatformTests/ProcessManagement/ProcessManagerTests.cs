// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using Metalama.Backstage;
using SharpCrafters.Backstage.Extensibility;
using SharpCrafters.Backstage.Maintenance;
using SharpCrafters.Backstage.Testing;
using Xunit;

namespace SharpCrafters.Backstage.PlatformTests.ProcessManagement;

/// <summary>
/// Runs the process manager of Linux, which reads the assemblies of a <c>dotnet</c> process from
/// <c>/proc/&lt;pid&gt;/cmdline</c>, and of macOS, which reads them from the output of <c>lsof</c>. The manager of
/// Windows reads the modules of the process and runs in the unit tests on a Windows host.
/// </summary>
/// <remarks>
/// <see cref="IProcessManager.KillCompilerProcesses"/> ends every process it recognizes on the machine, including the
/// compiler server and the MSBuild nodes of other builds. Run these tests in a container, or on an agent that runs one
/// build at a time.
/// </remarks>
public sealed class ProcessManagerTests : IDisposable
{
    private readonly string _directory = Path.Combine( Path.GetTempPath(), "PlatformTests", Guid.NewGuid().ToString( "N" ) );

    [PlatformFact( TestPlatforms.Unix )]
    public async Task AWorkerProcessIsFoundAndEnded()
    {
        var workerAssemblyPath = this.CreateWorkerAssembly();

        using var worker = HelperProcess.StartAssembly( workerAssemblyPath, HelperCommands.Wait );
        await worker.ReadUntilAsync( HelperCommands.ReadyLine );

        PlatformTestServices.CreateServiceProvider().GetRequiredBackstageService<IProcessManager>().KillCompilerProcesses( false );

        await worker.WaitForExitAsync();
    }

    /// <summary>
    /// Copies the helper under the name of the Backstage Worker, which the process manager recognizes when it runs under
    /// <c>dotnet</c>. The dependency file is not copied, because it names the helper; without it, the host resolves the
    /// dependencies from the directory of the assembly.
    /// </summary>
    private string CreateWorkerAssembly()
    {
        Directory.CreateDirectory( this._directory );

        var helperName = Path.GetFileNameWithoutExtension( HelperProcess.AssemblyPath );
        var workerName = $"{MetalamaProduct.Profile.ToolAssemblyNamePrefix}.Worker";

        foreach ( var file in Directory.GetFiles( AppContext.BaseDirectory ) )
        {
            var fileName = Path.GetFileName( file );

            if ( fileName.EndsWith( ".deps.json", StringComparison.Ordinal ) )
            {
                continue;
            }

            if ( fileName.StartsWith( helperName + ".", StringComparison.Ordinal ) )
            {
                fileName = workerName + fileName.Substring( helperName.Length );
            }

            File.Copy( file, Path.Combine( this._directory, fileName ) );
        }

        return Path.Combine( this._directory, workerName + ".dll" );
    }

    public void Dispose()
    {
        if ( Directory.Exists( this._directory ) )
        {
            Directory.Delete( this._directory, recursive: true );
        }
    }
}
