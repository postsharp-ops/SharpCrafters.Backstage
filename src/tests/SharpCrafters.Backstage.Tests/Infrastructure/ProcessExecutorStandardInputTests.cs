// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Infrastructure;
using SharpCrafters.Backstage.Testing;
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace SharpCrafters.Backstage.Tests.Infrastructure;

/// <summary>
/// The collection of <see cref="ProcessExecutorStandardInputTests"/>, which replaces the standard input of the test
/// process. A process that another test started meanwhile would inherit the replacement, so the collection runs alone.
/// </summary>
[CollectionDefinition( Name, DisableParallelization = true )]
public sealed class StandardInputCollection
{
    public const string Name = "Standard input";
}

/// <summary>
/// Verifies that <see cref="ProcessExecutor"/> does not let a process inherit the standard input of the current process.
/// </summary>
/// <remarks>
/// <para>
/// The PostSharp compiler reads its standard input to learn that the build is cancelled, and git, which the version
/// control check starts, inherited that standard input. On Windows, the operations on a handle opened for synchronous
/// I/O are serialized, so git blocked during its initialization until the timeout of the command expired, and the
/// project counted as modified.
/// </para>
/// <para>
/// The test replaces the standard input of the current process with an inheritable pipe that nothing writes to and that
/// stays open. <c>sort.exe</c> reads its standard input to its end, so it completes only when it reads a stream of its
/// own. The pipe must be inheritable: a handle that is not would give the child process an invalid standard input, and
/// the test would pass without the fix. The standard input of a process is replaced with <c>SetStdHandle</c>, which
/// exists on Windows only.
/// </para>
/// </remarks>
[Collection( StandardInputCollection.Name )]
public sealed class ProcessExecutorStandardInputTests : TestsBase
{
    private const int _standardInputHandle = -10;

    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds( 20 );

    public ProcessExecutorStandardInputTests( ITestOutputHelper logger ) : base( logger ) { }

    [PlatformTheory( TestPlatforms.Windows )]
    [InlineData( false )]
    [InlineData( true )]
    public async Task StandardInputIsNotInherited( bool isAsync )
    {
        using var pipe = new AnonymousPipeServerStream( PipeDirection.Out, HandleInheritability.Inheritable );

        var originalStandardInput = GetStdHandle( _standardInputHandle );

        Assert.True( SetStdHandle( _standardInputHandle, pipe.ClientSafePipeHandle.DangerousGetHandle() ) );

        try
        {
            var stopwatch = Stopwatch.StartNew();

            var startInfo = new ProcessStartInfo( "sort.exe" );

            var succeeded = isAsync
                ? await new ProcessExecutor().TryExecuteAsync( startInfo, _timeout, CancellationToken.None ) != null
                : new ProcessExecutor().TryExecute( startInfo, _timeout, out _ );

            var elapsed = stopwatch.Elapsed;
            this.Logger.WriteLine( $"The method returned after {elapsed}." );

            Assert.True( succeeded, "The process failed or did not complete before the timeout." );

            // The bound is generous, because the machine that runs the test is shared, but it is far below the timeout,
            // which is what a process that reads the inherited pipe reaches.
            Assert.True( elapsed < TimeSpan.FromSeconds( 15 ), $"The method returned after {elapsed}, so the process waited for its input." );
        }
        finally
        {
            SetStdHandle( _standardInputHandle, originalStandardInput );
        }
    }

    [DllImport( "kernel32.dll", SetLastError = true )]
    private static extern IntPtr GetStdHandle( int standardHandle );

    [DllImport( "kernel32.dll", SetLastError = true )]
    [return: MarshalAs( UnmanagedType.Bool )]
    private static extern bool SetStdHandle( int standardHandle, IntPtr handle );
}
