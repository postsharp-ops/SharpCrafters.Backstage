// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Diagnostics;
using SharpCrafters.Backstage.Extensibility;

namespace SharpCrafters.Backstage.FileLocks;

public static partial class RetryHelper
{
    private sealed class DeadlockDetectionContext
    {
        private readonly IServiceProvider? _serviceProvider;
        private readonly ILogger? _logger;
        private readonly IReadOnlyList<string> _files;
        private readonly bool _hasWarning;

        public DeadlockDetectionContext(
            IServiceProvider? serviceProvider,
            ILogger? logger,
            IReadOnlyList<string> files,
            RetryWarning? warning )
        {
            this._serviceProvider = serviceProvider;
            this._logger = logger;
            this._files = files;
            this._hasWarning = warning != null;

            // One reporter for the whole call, so that an operation retried once per file warns once and not once per
            // file.
            this.OnFailedAttempt = warning?.CreateReporter( () => this.LockingProcesses );
        }

        /// <summary>
        /// Gets the delegate invoked after each failed attempt, or <c>null</c> when the caller asked for no warning.
        /// </summary>
        public Action<int, TimeSpan, Exception>? OnFailedAttempt { get; }

        /// <summary>
        /// Gets the sentence naming the processes holding the files, found when the operation first failed, or
        /// <c>null</c>.
        /// </summary>
        public string? LockingProcesses { get; private set; }

        public void OnRecoverableException( Exception exception )
        {
            // Naming the processes costs a restart manager session, so it is done only when a logger or a warning reads
            // the result.
            if ( this.LockingProcesses != null || (this._logger == null && !this._hasWarning) )
            {
                return;
            }

            var lockingDetection = this._serviceProvider?.GetBackstageService<ILockingProcessDetector>();

            if ( lockingDetection == null )
            {
                return;
            }

            var lockingProcesses = lockingDetection.GetProcessesUsingFiles( this._files );

            if ( lockingProcesses.Count == 0 )
            {
                this._logger?.Trace?.Log( "No process locking these files was found." );

                return;
            }

            this.LockingProcesses = "The following process(es) are locking these files: " + string.Join(
                ", ",
                lockingProcesses.Select( p => $"{p.ProcessName} ({p.Id})" ) );

            this._logger?.Warning?.Log( this.LockingProcesses );
        }

        public void OnFatalException( Exception e )
        {
            var lockingDetection = this._serviceProvider?.GetBackstageService<ILockingProcessDetector>();

            if ( lockingDetection != null )
            {
                var lockingProcesses = lockingDetection.GetProcessesUsingFiles( this._files );

                if ( lockingProcesses.Count > 0 )
                {
                    var additionalMessage =
                        $" The following process(es) are locking the file(s): {string.Join( ", ", lockingProcesses.Select( p => $"{p.ProcessName} ({p.Id})" ) )}.";

                    throw new LockedFileException( e.Message + additionalMessage, e );
                }
            }
        }
    }
}
