// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Application;
using SharpCrafters.Backstage.Configuration;
using SharpCrafters.Backstage.Diagnostics;
using SharpCrafters.Backstage.Extensibility;
using SharpCrafters.Backstage.Infrastructure;
using SharpCrafters.Backstage.Maintenance;
using SharpCrafters.Backstage.Testing;
using System;
using System.IO;
using Xunit;

namespace SharpCrafters.Backstage.Tests.MiniDump;

public sealed class MiniDumpTests : TestsBase, IDisposable
{
    private readonly DiskTempFileManager _tempFileManager = new();

    public MiniDumpTests( ITestOutputHelper logger ) : base( logger ) { }

    public void Dispose()
    {
        if ( Directory.Exists( this._tempFileManager.Root ) )
        {
            Directory.Delete( this._tempFileManager.Root, true );
        }
    }

    protected override void ConfigureServices( ServiceProviderBuilder services )
    {
        services
            .AddSingleton<IApplicationInfoProvider>( new ApplicationInfoProvider( new TestApplicationInfo() ) )
            .AddSingleton<IConfigurationManager>( serviceProvider => new InMemoryConfigurationManager( serviceProvider ) )
            // The runtime writes the dump to the disk, not to the file system of the test.
            .AddSingleton<ITempFileManager>( this._tempFileManager )
            .AddSingleton<IPlatformInfo>( serviceProvider => new PlatformInfo( serviceProvider ) );
    }

    /// <summary>
    /// On the .NET runtime, the dump is written through the diagnostics channel of the runtime, without the dotnet-dump tool.
    /// </summary>
    [Fact]
    public void WhenWriteFileExists()
    {
        if ( System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription.StartsWith( ".NET Framework", StringComparison.Ordinal ) )
        {
            // A .NET Framework process has no diagnostics channel, and needs dotnet-dump on the agent.
            return;
        }

        var serviceProvider = this.ServiceProvider;
        var dumper = new MiniDumper( serviceProvider );

        try
        {
#pragma warning disable CA2201
            throw new Exception();
#pragma warning restore CA2201
        }
        catch
        {
            var dumpFile = dumper.Write();

            this.Logger.WriteLine( $"Dump file '{dumpFile}' written." );
            Assert.NotNull( dumpFile );
            Assert.True( File.Exists( dumpFile ) );
        }
    }

    /// <summary>
    /// A temporary directory on the disk, which the dump needs, because the runtime writes it.
    /// </summary>
    private sealed class DiskTempFileManager : ITempFileManager
    {
        public string Root { get; } = Path.Combine( Path.GetTempPath(), "SharpCrafters.Backstage.Tests.MiniDump", Guid.NewGuid().ToString( "N" ) );

        public string GetTempDirectory(
            string directory,
            CleanUpStrategy cleanUpStrategy,
            string? subdirectory = null,
            TempFileVersionScope versionScope = TempFileVersionScope.Default )
        {
            var path = Path.Combine( this.Root, directory, subdirectory ?? "" );
            Directory.CreateDirectory( path );

            return path;
        }
    }
}