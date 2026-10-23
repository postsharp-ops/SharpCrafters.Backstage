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
using System.Collections.Immutable;
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
        using var dumper = new MiniDumper( serviceProvider );

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
    /// When the type of an exception thrown while a dump is written is one that makes a dump, the exception does not make
    /// the first-chance handler start another dump: the handler would otherwise call <see cref="MiniDumper.Write"/> again,
    /// recursively, on the same thread.
    /// </summary>
    /// <remarks>
    /// The dumper of this test makes a dump of <see cref="DumpWhileWritingException"/> only, and not of every exception
    /// type. The first-chance handler asks every live dumper of the process, so a dumper of every exception type would
    /// also try to write a dump for each exception of the tests that run at the same time, from inside the handler. On
    /// .NET Framework, the runtime terminates the process when that attempt throws.
    /// </remarks>
    [Fact]
    public void AnExceptionWhileWritingDoesNotStartAnotherDump()
    {
        var serviceProvider = this.ServiceProvider;
        var processKind = serviceProvider.GetRequiredBackstageService<IApplicationInfoProvider>().ProcessKind.ToString();

        ((InMemoryConfigurationManager) serviceProvider.GetRequiredBackstageService<IConfigurationManager>()).Set(
            new DiagnosticsConfiguration
            {
                CrashDumps = new CrashDumpConfiguration
                {
                    Processes = ImmutableDictionary<string, bool>.Empty.WithComparers( StringComparer.OrdinalIgnoreCase ).Add( processKind, true ),
                    ExceptionTypes = ImmutableArray.Create( nameof(DumpWhileWritingException) )
                }
            } );

        var testThreadId = Environment.CurrentManagedThreadId;
        var depth = 0;
        var maxDepth = 0;

        // The first step of a dump throws. Beyond a depth of 3, it throws an IOException, for which no dump is written, so
        // that a regression fails this test instead of overflowing the stack.
        this._tempFileManager.OnGetTempDirectory = () =>
        {
            if ( Environment.CurrentManagedThreadId != testThreadId )
            {
                // A first-chance exception of a test that runs at the same time.
                throw new IOException();
            }

            depth++;
            maxDepth = Math.Max( maxDepth, depth );

            try
            {
                if ( depth > 3 )
                {
                    throw new IOException();
                }

                throw new DumpWhileWritingException();
            }
            finally
            {
                depth--;
            }
        };

        var dumper = new MiniDumper( serviceProvider );

        try
        {
            Assert.Null( dumper.Write() );
            Assert.Equal( 1, maxDepth );
        }
        finally
        {
            this._tempFileManager.OnGetTempDirectory = null;

            // The first-chance handler stays subscribed for the process: remove the enabled dumper from the ones it considers.
            dumper.Dispose();
        }
    }

    /// <summary>
    /// The exception that the first step of a dump throws, which is the only one that the dumper of the test writes a dump
    /// of.
    /// </summary>
    private sealed class DumpWhileWritingException : Exception;

    /// <summary>
    /// A temporary directory on the disk, which the dump needs, because the runtime writes it.
    /// </summary>
    private sealed class DiskTempFileManager : ITempFileManager
    {
        public Action? OnGetTempDirectory { get; set; }

        public string Root { get; } = Path.Combine( Path.GetTempPath(), "SharpCrafters.Backstage.Tests.MiniDump", Guid.NewGuid().ToString( "N" ) );

        public string GetTempDirectory(
            string directory,
            CleanUpStrategy cleanUpStrategy,
            string? subdirectory = null,
            TempFileVersionScope versionScope = TempFileVersionScope.Default )
        {
            this.OnGetTempDirectory?.Invoke();

            var path = Path.Combine( this.Root, directory, subdirectory ?? "" );
            Directory.CreateDirectory( path );

            return path;
        }
    }
}