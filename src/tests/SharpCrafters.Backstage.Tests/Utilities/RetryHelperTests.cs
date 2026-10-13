// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Diagnostics;
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

    [Fact]
    public void AWarningWhoseThresholdIsTheLastAttemptIsReported()
    {
        var warnings = new List<RetryWarningContext>();

        Assert.Throws<IOException>(
            () => RetryHelper.Retry(
                () => throw new IOException(),
                warning: RetryWarning.AfterAttempts( 3, warnings.Add ),
                budget: RetryBudget.OfAttempts( 3 ) ) );

        var warning = Assert.Single( warnings );
        Assert.Equal( 3, warning.Attempts );
    }

    [Fact]
    public void AnExceptionThatIsNotRetriedDoesNotWarn()
    {
        var warnings = new List<RetryWarningContext>();

        Assert.Throws<InvalidOperationException>(
            () => RetryHelper.Retry(
                () => throw new InvalidOperationException(),
                warning: RetryWarning.AfterAttempts( 1, warnings.Add ) ) );

        Assert.Empty( warnings );
    }

    [Fact]
    public void TheAttemptsOfEveryFileCountTowardsTheWarning()
    {
        // Each file fails twice and then succeeds. No file reaches the threshold alone, and the call does.
        var failures = new Dictionary<string, int>();
        var warnings = new List<RetryWarningContext>();

        RetryHelper.RetryWithLockDetection(
            new[] { "a.txt", "b.txt" },
            file => FailTwice( failures, file ),
            null,
            warning: RetryWarning.AfterAttempts( 3, warnings.Add ) );

        var warning = Assert.Single( warnings );
        Assert.Equal( 3, warning.Attempts );
    }

    [Fact]
    public void TheDurationOfTheBudgetIsCountedForTheWholeCall()
    {
        // Each file fails twice and then succeeds. The clock advances by the waits only: the first file fails at 0 ms
        // and 10 ms and succeeds at 22 ms. The second file fails at 22 ms, waits the 8 ms left in the budget, and fails
        // again at 30 ms, when the budget is spent. A budget counted for each file would let it succeed at 32 ms.
        var failures = new Dictionary<string, int>();
        var clock = new TestRetryClock();

        Assert.Throws<IOException>(
            () => RetryHelper.RetryWithLockDetection(
                new[] { "a.txt", "b.txt" },
                file => FailTwice( failures, file ),
                null,
                null,
                null,
                null,
                RetryBudget.OfDuration( TimeSpan.FromMilliseconds( 30 ) ),
                clock ) );

        Assert.Equal( 2, failures["b.txt"] );
        Assert.Equal( TimeSpan.FromMilliseconds( 30 ), clock.Elapsed );
    }

    [Fact]
    public void TheWarningTimeIsCountedForTheWholeCall()
    {
        // The first file fails at 0 ms and 10 ms and succeeds at 22 ms. The second file fails at 22 ms and 32 ms. Only
        // the time of the whole call reaches the threshold: the second file alone has been failing for 10 ms.
        var failures = new Dictionary<string, int>();
        var warnings = new List<RetryWarningContext>();

        RetryHelper.RetryWithLockDetection(
            new[] { "a.txt", "b.txt" },
            file => FailTwice( failures, file ),
            null,
            null,
            null,
            RetryWarning.AfterDuration( TimeSpan.FromMilliseconds( 30 ), warnings.Add ),
            null,
            new TestRetryClock() );

        var warning = Assert.Single( warnings );
        Assert.Equal( TimeSpan.FromMilliseconds( 32 ), warning.Elapsed );
        Assert.Equal( 4, warning.Attempts );
    }

    [Fact]
    public void ADurationBudgetStopsRetryingWithoutOvershootingTheDeadline()
    {
        // The waits are 10, 12, 14.4 and then the 13.6 ms left in the budget, so the fifth attempt fails at exactly 50 ms.
        var attempts = 0;
        var clock = new TestRetryClock();

        Assert.Throws<IOException>(
            () => RetryHelper.Retry<bool>(
                () =>
                {
                    attempts++;

                    throw new IOException();
                },
                null,
                null,
                null,
                null,
                RetryBudget.OfDuration( TimeSpan.FromMilliseconds( 50 ) ),
                clock ) );

        Assert.Equal( 5, attempts );
        Assert.Equal( TimeSpan.FromMilliseconds( 50 ), clock.Elapsed );
    }

    [Fact]
    public void TheTraceReportsTheStartTheChangesAndTheEndOfTheRetries()
    {
        // Three failures with the same error are one line, and a fourth with another error is another line.
        var failures = 0;
        var logger = new TraceLogger();

        var result = RetryHelper.Retry(
            () => ++failures switch
            {
                <= 3 => throw new IOException( "Locked.", unchecked( (int) 0x80070020 ) ),
                4 => throw new IOException( "Denied.", unchecked( (int) 0x80070005 ) ),
                _ => 42
            },
            logger: logger );

        Assert.Equal( 42, result );
        Assert.Collection(
            logger.TraceLines,
            line => Assert.StartsWith( "Attempt 1 failed after ", line, StringComparison.Ordinal ),
            line =>
            {
                Assert.StartsWith( "Attempt 4 failed after ", line, StringComparison.Ordinal );
                Assert.Contains( "0x80070005", line, StringComparison.Ordinal );
            },
            line => Assert.StartsWith( "Attempt 5 succeeded after ", line, StringComparison.Ordinal ) );
    }

    [Fact]
    public void TheTraceReportsAnOperationThatIsNotRetried()
    {
        var logger = new TraceLogger();

        Assert.Throws<InvalidOperationException>( () => RetryHelper.Retry( () => throw new InvalidOperationException(), logger: logger ) );

        Assert.StartsWith( "Attempt 1 failed with an error that is not retried: ", Assert.Single( logger.TraceLines ), StringComparison.Ordinal );
    }

    [Fact]
    public void TheTraceReportsTheEndOfTheBudget()
    {
        var logger = new TraceLogger();

        Assert.Throws<IOException>( () => RetryHelper.Retry( () => throw new IOException(), logger: logger, budget: RetryBudget.OfAttempts( 3 ) ) );

        Assert.StartsWith( "Giving up after 3 attempt(s) and ", logger.TraceLines[logger.TraceLines.Count - 1], StringComparison.Ordinal );
    }

    [Fact]
    public void TheSettingsReplaceTheTimeLimitOfTheBudget()
    {
        var clock = new TestRetryClock();

        Assert.Throws<IOException>(
            () => RetryHelper.RetryWithLockDetection<bool>(
                _files,
                () => throw new IOException(),
                new SettingsServiceProvider( TimeSpan.FromMilliseconds( 30 ), null ),
                null,
                null,
                null,
                RetryBudget.OfDuration( TimeSpan.FromSeconds( 10 ) ),
                clock ) );

        Assert.Equal( TimeSpan.FromMilliseconds( 30 ), clock.Elapsed );
    }

    [Fact]
    public void TheSettingsDoNotChangeABudgetOfAttempts()
    {
        var attempts = 0;

        Assert.Throws<IOException>(
            () => RetryHelper.RetryWithLockDetection<bool>(
                _files,
                () =>
                {
                    attempts++;

                    throw new IOException();
                },
                new SettingsServiceProvider( TimeSpan.FromMilliseconds( 1 ), null ),
                null,
                null,
                null,
                RetryBudget.OfAttempts( 4 ),
                new TestRetryClock() ) );

        Assert.Equal( 4, attempts );
    }

    [Fact]
    public void AZeroWarningThresholdWarnsAtTheFirstFailedAttempt()
    {
        var failures = 0;
        var warnings = new List<RetryWarningContext>();

        RetryHelper.RetryWithLockDetection(
            _files,
            () => ++failures <= 2 ? throw new IOException() : 42,
            new SettingsServiceProvider( null, TimeSpan.Zero ),
            null,
            null,
            RetryWarning.AfterAttempts( 10, warnings.Add ),
            RetryBudget.OfDuration( TimeSpan.FromSeconds( 10 ) ),
            new TestRetryClock() );

        Assert.Equal( 1, Assert.Single( warnings ).Attempts );
    }

    [Fact]
    public void AWarningThresholdThatIsNotBelowTheTimeLimitDoesNotWarn()
    {
        var warnings = new List<RetryWarningContext>();

        Assert.Throws<IOException>(
            () => RetryHelper.RetryWithLockDetection<bool>(
                _files,
                () => throw new IOException(),
                new SettingsServiceProvider( TimeSpan.FromMilliseconds( 50 ), TimeSpan.FromMilliseconds( 50 ) ),
                null,
                null,
                RetryWarning.AfterAttempts( 1, warnings.Add ),
                RetryBudget.OfDuration( TimeSpan.FromSeconds( 10 ) ),
                new TestRetryClock() ) );

        Assert.Empty( warnings );
    }

    [Fact]
    public void TheFirstAttemptRunsWhateverTheTimeLimit()
    {
        var attempts = 0;
        var warnings = new List<RetryWarningContext>();

        var result = RetryHelper.RetryWithLockDetection(
            _files,
            () => ++attempts,
            new SettingsServiceProvider( TimeSpan.FromMilliseconds( 1 ), TimeSpan.Zero ),
            null,
            null,
            RetryWarning.AfterAttempts( 1, warnings.Add ),
            RetryBudget.OfDuration( TimeSpan.FromSeconds( 10 ) ),
            new TestRetryClock() );

        Assert.Equal( 1, result );
        Assert.Empty( warnings );
    }

    private static void FailTwice( Dictionary<string, int> failures, string file )
    {
        failures.TryGetValue( file, out var count );

        if ( count < 2 )
        {
            failures[file] = count + 1;

            throw new IOException();
        }
    }

    /// <summary>
    /// A clock that advances by the time that the retry loop asks to wait, without waiting.
    /// </summary>
    private sealed class TestRetryClock : IRetryClock
    {
        public TimeSpan Elapsed { get; private set; }

        public void Sleep( TimeSpan duration ) => this.Elapsed += duration;
    }

    private sealed class LockingServiceProvider : IServiceProvider, ILockingProcessDetector
    {
        public object? GetService( Type serviceType ) => serviceType == typeof(ILockingProcessDetector) ? this : null;

        public IReadOnlyList<Process> GetProcessesUsingFiles( IReadOnlyList<string> filePaths ) => new[] { Process.GetCurrentProcess() };
    }

    private sealed class SettingsServiceProvider : IServiceProvider, IFileLockRetrySettings
    {
        public SettingsServiceProvider( TimeSpan? timeout, TimeSpan? warningThreshold )
        {
            this.Timeout = timeout;
            this.WarningThreshold = warningThreshold;
        }

        public TimeSpan? Timeout { get; }

        public TimeSpan? WarningThreshold { get; }

        public object? GetService( Type serviceType ) => serviceType == typeof(IFileLockRetrySettings) ? this : null;
    }

    /// <summary>
    /// A logger that records what is written to its <c>Trace</c> writer.
    /// </summary>
    private sealed class TraceLogger : ILogger, ILogWriter
    {
        public List<string> TraceLines { get; } = new();

        public ILogWriter Trace => this;

        public ILogWriter? Info => null;

        public ILogWriter? Warning => null;

        public ILogWriter? Error => null;

        public ILogger WithPrefix( string prefix ) => this;

        public void Log( string message ) => this.TraceLines.Add( message );
    }
}
