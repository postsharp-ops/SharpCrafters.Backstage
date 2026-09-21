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
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger? _logger;
        private readonly IReadOnlyList<string> _files;
        private readonly Action<string>? _onFilesLocked;

        public DeadlockDetectionContext(
            IServiceProvider serviceProvider,
            ILogger? logger,
            IReadOnlyList<string> files,
            Action<string>? onFilesLocked = null )
        {
            this._serviceProvider = serviceProvider;
            this._logger = logger;
            this._files = files;
            this._onFilesLocked = onFilesLocked;
        }

        public void OnRecoverableException( Exception exception )
        {
            var lockingDetection = this._serviceProvider.GetBackstageService<ILockingProcessDetector>();

            if ( lockingDetection == null || (this._logger == null && this._onFilesLocked == null) )
            {
                return;
            }

            var lockingProcesses = lockingDetection.GetProcessesUsingFiles( this._files );

            if ( lockingProcesses.Count == 0 )
            {
                this._logger?.Trace?.Log( "No process locking these files was found." );

                return;
            }

            var message = "The following process(es) are locking these files: " + string.Join(
                ", ",
                lockingProcesses.Select( p => $"{p.ProcessName} ({p.Id})" ) );

            this._logger?.Warning?.Log( message );

            // A product whose diagnostics are not a log reports it itself. It is called once, when the operation
            // first fails, and not on every attempt, so a caller that turns this into a message for the user does
            // not produce one per retry.
            this._onFilesLocked?.Invoke( message );
        }

        public void OnFatalException( Exception e )
        {
            var lockingDetection = this._serviceProvider.GetBackstageService<ILockingProcessDetector>();

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