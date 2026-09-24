// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.FileLocks;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Xunit;

namespace SharpCrafters.Backstage.Tests.Utilities;

/// <summary>
/// An operation on a file that another process holds for a moment succeeds without troubling the user. When the file
/// stays held, the user is told once, and is told which process holds it.
/// </summary>
public sealed class RetryHelperTests
{
    private static readonly string[] _files = { "a.txt" };

    [Fact]
    public void ReturnsTheValueOfAnOperationThatSucceedsAfterFailing()
    {
        var failures = 0;
        var warnings = new List<RetryWarningContext>();

        var result = RetryHelper.Retry(
            () => ++failures <= 2 ? throw new IOException() : 42,
            warning: RetryWarning.AfterAttempts( 5, warnings.Add ) );

        Assert.Equal( 42, result );
        Assert.Empty( warnings );
    }

    [Fact]
    public void WarnsOnceWhenTheThresholdIsReached()
    {
        var warnings = new List<RetryWarningContext>();

        Assert.Throws<IOException>(
            () => RetryHelper.Retry(
                () => throw new IOException(),
                warning: RetryWarning.AfterAttempts( 3, warnings.Add ),
                budget: RetryBudget.OfAttempts( 6 ) ) );

        var warning = Assert.Single( warnings );
        Assert.Equal( 3, warning.Attempts );
        Assert.IsType<IOException>( warning.Exception );
        Assert.Null( warning.LockingProcesses );
    }

    [Fact]
    public void WarningNamesTheProcessesHoldingTheFiles()
    {
        var failures = 0;
        var warnings = new List<RetryWarningContext>();

        var result = RetryHelper.RetryWithLockDetection(
            _files,
            () => ++failures <= 3 ? throw new IOException() : 42,
            new LockingServiceProvider(),
            warning: RetryWarning.AfterAttempts( 2, warnings.Add ) );

        Assert.Equal( 42, result );
        var warning = Assert.Single( warnings );
        Assert.Contains( Process.GetCurrentProcess().ProcessName, warning.LockingProcesses, StringComparison.Ordinal );
    }

    [Fact]
    public void WorksWithoutServices()
    {
        var failures = 0;
        var warnings = new List<RetryWarningContext>();

        var result = RetryHelper.RetryWithLockDetection(
            _files,
            () => ++failures <= 3 ? throw new IOException() : 42,
            null,
            warning: RetryWarning.AfterAttempts( 2, warnings.Add ) );

        Assert.Equal( 42, result );
        Assert.Null( Assert.Single( warnings ).LockingProcesses );
    }

    [Fact]
    public void AnOperationRetriedForEachFileWarnsOnce()
    {
        var failures = new Dictionary<string, int>();
        var warnings = new List<RetryWarningContext>();

        RetryHelper.RetryWithLockDetection(
            new[] { "a.txt", "b.txt" },
            file =>
            {
                failures.TryGetValue( file, out var count );
                failures[file] = count + 1;

                if ( count < 3 )
                {
                    throw new IOException();
                }
            },
            null,
            warning: RetryWarning.AfterAttempts( 2, warnings.Add ) );

        Assert.Single( warnings );
    }

    [Fact]
    public void AFileThatStaysHeldNamesTheProcessesInTheException()
    {
        var exception = Assert.Throws<LockedFileException>(
            () => RetryHelper.RetryWithLockDetection(
                _files,
                () => throw new IOException( "The file is locked." ),
                new LockingServiceProvider(),
                budget: RetryBudget.OfAttempts( 2 ) ) );

        Assert.Contains( Process.GetCurrentProcess().ProcessName, exception.Message, StringComparison.Ordinal );
    }

    private sealed class LockingServiceProvider : IServiceProvider, ILockingProcessDetector
    {
        public object? GetService( Type serviceType ) => serviceType == typeof(ILockingProcessDetector) ? this : null;

        public IReadOnlyList<Process> GetProcessesUsingFiles( IReadOnlyList<string> filePaths ) => new[] { Process.GetCurrentProcess() };
    }
}
