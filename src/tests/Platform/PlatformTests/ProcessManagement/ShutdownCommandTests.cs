// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using Metalama.Backstage;
using SharpCrafters.Backstage.Commands;
using SharpCrafters.Backstage.Testing;
using Spectre.Console;
using Spectre.Console.Cli;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Xunit;
using Xunit.Abstractions;

namespace SharpCrafters.Backstage.PlatformTests.ProcessManagement;

/// <summary>
/// Runs the <c>shutdown</c> command, as a user runs it, against the processes that it stops on Linux and macOS: the worker,
/// which the process manager recognizes from the assemblies of a <c>dotnet</c> process, read from
/// <c>/proc/&lt;pid&gt;/cmdline</c> on Linux and from the output of <c>lsof</c> on macOS, and the build servers of the .NET
/// SDK. The process manager of Windows reads the modules of the process and runs in the unit tests on a Windows host.
/// </summary>
/// <remarks>
/// The command stops every process it recognizes on the machine, including the compiler server and the MSBuild nodes of
/// other builds. Run these tests in a container, or on an agent that runs one build at a time.
/// </remarks>
public sealed class ShutdownCommandTests : IDisposable
{
    // Its own executable in the current SDK, and an assembly under 'dotnet' in some earlier ones.
    private static readonly Regex _compilerServer = new( @"/VBCSCompiler(\.dll)?(\s|$)" );

    private static readonly Regex _msbuildNode = new( @"MSBuild\.dll.*nodemode", RegexOptions.IgnoreCase );

    private readonly string _directory = Path.Combine( Path.GetTempPath(), "PlatformTests", Guid.NewGuid().ToString( "N" ) );
    private readonly ITestOutputHelper _output;

    public ShutdownCommandTests( ITestOutputHelper output )
    {
        this._output = output;
    }

    /// <summary>
    /// The worker is ended, even without <c>--force</c>, because it holds no state that ending it can lose. A worker that is
    /// left running keeps the files of the product locked, and the setup web server keeps running for as long as its page
    /// is open.
    /// </summary>
    [PlatformFact( TestPlatforms.Unix )]
    public async Task TheWorkerIsEnded()
    {
        var workerAssemblyPath = this.CreateWorkerAssembly();

        using var worker = HelperProcess.StartAssembly( workerAssemblyPath, HelperCommands.Wait );
        await worker.ReadUntilAsync( HelperCommands.ReadyLine );

        var output = this.RunShutdown();

        Assert.Contains( $"Metalama worker (process {worker.Process.Id}): ended.", output, StringComparison.Ordinal );
        await worker.WaitForExitAsync();
    }

    /// <summary>
    /// The MSBuild nodes and the compiler server that a build leaves running exit on request, and the command succeeds
    /// without <c>--force</c>. Each of them keeps the assemblies of the build loaded, which is what locks the files of the
    /// product after a build.
    /// </summary>
    /// <remarks>
    /// In a container only, because the build servers are shared by every build of the user, and on Linux only, where the
    /// command lines of the processes are read from <c>/proc</c> to check the result.
    /// </remarks>
    [PlatformFact( TestPlatforms.Linux, TestHosts.Container )]
    public async Task TheBuildServersExit()
    {
        await this.BuildAsync();

        var nodes = GetProcessIds( _msbuildNode );
        var compilerServers = GetProcessIds( _compilerServer );

        // Without these processes, the test would show nothing.
        Assert.NotEmpty( nodes );
        Assert.NotEmpty( compilerServers );

        var output = this.RunShutdown();

        foreach ( var node in nodes )
        {
            Assert.Contains( $"MSBuild node (process {node}): exited.", output, StringComparison.Ordinal );
        }

        foreach ( var compilerServer in compilerServers )
        {
            Assert.Contains( $"Compiler server (VBCSCompiler) (process {compilerServer}): exited.", output, StringComparison.Ordinal );
        }

        Assert.Empty( GetProcessIds( _msbuildNode ) );
        Assert.Empty( GetProcessIds( _compilerServer ) );
    }

    /// <summary>
    /// Runs the command in this process and returns what it wrote. It fails the test when the command reports that a
    /// process is still running.
    /// </summary>
    private string RunShutdown()
    {
        var output = new StringWriter();
        var app = new CommandApp();

        BackstageCommandFactory.ConfigureCommandApp(
            app,
            new BackstageCommandOptions(
                PlatformTestServices.ApplicationInfo,
                MetalamaProduct.Instance,
                standardOutput: output,
                errorOutput: output,
                ansiSupport: AnsiSupport.No ) );

        var exitCode = app.Run( ["shutdown", "--timeout", "60"] );

        this._output.WriteLine( output.ToString() );
        Assert.Equal( 0, exitCode );

        return output.ToString();
    }

    /// <summary>
    /// Builds four projects in parallel with node reuse, so that MSBuild starts nodes of its own rather than building in the
    /// process of the command, and the compiler server, which the SDK uses by default, is started.
    /// </summary>
    private async Task BuildAsync()
    {
        var projects = new List<string>();

        foreach ( var name in new[] { "A", "B", "C", "D" } )
        {
            var projectDirectory = Path.Combine( this._directory, name );
            Directory.CreateDirectory( projectDirectory );

            await File.WriteAllTextAsync(
                Path.Combine( projectDirectory, $"{name}.csproj" ),
                """<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>""" );

            await File.WriteAllTextAsync( Path.Combine( projectDirectory, "Class.cs" ), $"public class {name} {{ }}" );
            projects.Add( $"{name}/{name}.csproj" );
        }

        // DefaultTargets matters: after -restore, MSBuild runs the default target, which would otherwise be the first one.
        await File.WriteAllTextAsync(
            Path.Combine( this._directory, "All.proj" ),
            $"""
             <Project DefaultTargets="Build">
               <ItemGroup><ProjectToBuild Include="{string.Join( ";", projects )}" /></ItemGroup>
               <Target Name="Restore"><MSBuild Projects="@(ProjectToBuild)" Targets="Restore" /></Target>
               <Target Name="Build"><MSBuild Projects="@(ProjectToBuild)" BuildInParallel="true" /></Target>
             </Project>
             """ );

        var startInfo = new ProcessStartInfo( "dotnet" ) { WorkingDirectory = this._directory, UseShellExecute = false };

        foreach ( var argument in new[] { "build", "All.proj", "-restore", "-m:4", "-nodeReuse:true", "-p:UseSharedCompilation=true" } )
        {
            startInfo.ArgumentList.Add( argument );
        }

        // The tests run under 'dotnet test --disable-build-servers', whose MSBuild variables the test host inherits. They
        // would turn the build servers off in this build too, and make it build with the MSBuild of the test run.
        foreach ( var name in startInfo.Environment.Keys.ToList() )
        {
            if ( name.StartsWith( "MSBUILD", StringComparison.OrdinalIgnoreCase )
                 || name.StartsWith( "DOTNET_CLI", StringComparison.OrdinalIgnoreCase )
                 || name.Equals( "UseSharedCompilation", StringComparison.OrdinalIgnoreCase ) )
            {
                startInfo.Environment.Remove( name );
            }
        }

        using var build = Process.Start( startInfo )!;
        await build.WaitForExitAsync();

        Assert.Equal( 0, build.ExitCode );
    }

    /// <summary>
    /// Gets the processes whose command line matches <paramref name="pattern"/>, read from <c>/proc</c>. A process that has
    /// exited and not yet been reaped has an empty command line, so it does not match.
    /// </summary>
    private static List<int> GetProcessIds( Regex pattern )
    {
        var processIds = new List<int>();

        foreach ( var directory in Directory.GetDirectories( "/proc" ) )
        {
            if ( !int.TryParse( Path.GetFileName( directory ), out var processId ) )
            {
                continue;
            }

            string commandLine;

            try
            {
                commandLine = File.ReadAllText( Path.Combine( directory, "cmdline" ) ).Replace( '\0', ' ' );
            }
            catch ( IOException )
            {
                // The process exited while it was being read.
                continue;
            }

            if ( pattern.IsMatch( commandLine ) )
            {
                processIds.Add( processId );
            }
        }

        return processIds;
    }

    /// <summary>
    /// Copies the helper under the name of the worker, which the process manager recognizes when it runs under
    /// <c>dotnet</c>. The dependency file is not copied, because it names the helper; without it, the host resolves the
    /// dependencies from the directory of the assembly.
    /// </summary>
    private string CreateWorkerAssembly()
    {
        var workerDirectory = Path.Combine( this._directory, "Worker" );
        Directory.CreateDirectory( workerDirectory );

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

            File.Copy( file, Path.Combine( workerDirectory, fileName ) );
        }

        return Path.Combine( workerDirectory, workerName + ".dll" );
    }

    public void Dispose()
    {
        if ( Directory.Exists( this._directory ) )
        {
            Directory.Delete( this._directory, recursive: true );
        }
    }
}
