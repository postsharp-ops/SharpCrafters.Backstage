// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using JetBrains.Annotations;
using SharpCrafters.Backstage.Infrastructure;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;

namespace SharpCrafters.Backstage.Testing;

[PublicAPI]
public class TestProcessExecutor : IProcessExecutor
{
    public List<ProcessStartInfo> StartedProcesses { get; } = [];

    /// <summary>
    /// Gets or sets an exception thrown instead of starting the process, so that a test can exercise what happens when
    /// a process cannot be started, e.g. because the tools have not been extracted.
    /// </summary>
    public Exception? ExceptionToThrow { get; set; }

    /// <summary>
    /// Gets or sets the function that returns the standard output of a process started by
    /// <see cref="TryExecute"/>, or <c>null</c> when the process is expected to fail. The default value
    /// returns <c>null</c> for every process.
    /// </summary>
    /// <remarks>
    /// A test of a command that runs on another operating system than the one that runs the test sets this property,
    /// so that the code under test observes the output of that command without the command being executed.
    /// </remarks>
    public Func<ProcessStartInfo, string?> StandardOutputProvider { get; set; } = _ => null;

    /// <summary>
    /// Gets or sets the function that serves <see cref="TryExecuteAsync"/>, so that a test can make the call
    /// block and observe what happens when it is cancelled. The default value is <c>null</c>, in which case
    /// <see cref="StandardOutputProvider"/> serves the asynchronous path too and it completes immediately.
    /// </summary>
    public Func<ProcessStartInfo, CancellationToken, Task<string?>>? AsyncStandardOutputProvider { get; set; }

    public IProcess Start( ProcessStartInfo startInfo )
    {
        if ( this.ExceptionToThrow != null )
        {
            throw this.ExceptionToThrow;
        }

        this.StartedProcesses.Add( startInfo );

        return new TestProcess();
    }

    public bool TryExecute( ProcessStartInfo startInfo, TimeSpan timeout, [NotNullWhen( true )] out string? standardOutput )
    {
        if ( this.ExceptionToThrow != null )
        {
            throw this.ExceptionToThrow;
        }

        this.StartedProcesses.Add( startInfo );

        standardOutput = this.StandardOutputProvider( startInfo );

        return standardOutput != null;
    }

    public Task<string?> TryExecuteAsync( ProcessStartInfo startInfo, TimeSpan timeout, CancellationToken cancellationToken )
    {
        cancellationToken.ThrowIfCancellationRequested();

        if ( this.ExceptionToThrow != null )
        {
            throw this.ExceptionToThrow;
        }

        this.StartedProcesses.Add( startInfo );

        if ( this.AsyncStandardOutputProvider != null )
        {
            return this.AsyncStandardOutputProvider( startInfo, cancellationToken );
        }

        return Task.FromResult( this.StandardOutputProvider( startInfo ) );
    }

    private sealed class TestProcess : IProcess
    {
        public void Dispose() { }

        public int ExitCode => 0;

        event Action? IProcess.Exited { add { } remove { } }

        public bool HasExited => false;

        public void WaitForExit() { }
    }
}
