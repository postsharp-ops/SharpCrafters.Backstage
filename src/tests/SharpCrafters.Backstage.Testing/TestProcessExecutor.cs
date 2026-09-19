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

        // The asynchronous path completes at once. A test that needs the command to still be running while something
        // else happens holds the code under test at one of its synchronization points, rather than making this
        // method block: an executor that blocks leaves the test racing whatever it does next against the completion
        // of the call, and the outcome then depends on the machine.
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
