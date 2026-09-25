// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Diagnostics;
using System;
using System.Diagnostics;
using System.Threading;

namespace SharpCrafters.Backstage.Maintenance;

internal abstract partial class ProcessManagerBase
{
    /// <summary>
    /// A process that matches a <see cref="KillableProcessSpec"/>. It owns <see cref="Process"/> and disposes it when it is
    /// disposed.
    /// </summary>
    protected sealed class KillableProcess : IDisposable
    {
        private readonly ILogger _logger;
        private readonly string? _mainModule;

        public KillableProcessSpec Spec { get; }

        public Process Process { get; }

        private bool Shutdown()
        {
            try
            {
                if ( this.Process.HasExited )
                {
                    return true;
                }

                this._logger.Trace?.Log( $"Gracefully shutting down process {this.Process.Id}." );

                var shutdownProcess = new Process()
                {
                    StartInfo = new ProcessStartInfo()
                    {
                        FileName = this.Process.MainModule!.FileName,
                        Arguments = this._mainModule != null ? $"\"{this._mainModule}\" -shutdown" : "-shutdown",
                        RedirectStandardOutput = true
                    }
                };

                shutdownProcess.Start();
                shutdownProcess.WaitForExit();

                var waitCycles = 0;

                while ( !this.Process.HasExited )
                {
                    if ( waitCycles > 5 )
                    {
                        return false;
                    }

                    waitCycles++;

                    Thread.Sleep( 1 );
                }

                return true;
            }
            catch ( Exception e )
            {
                this._logger.Warning?.Log( $"Unable to gracefully shut down process {this.Process.Id}: {e.Message}" );

                return false;
            }
        }

        private bool Kill( out string? errorMessage )
        {
            var process = this.Process;
            errorMessage = null;

            if ( process.HasExited )
            {
                return true;
            }

            try
            {
                this._logger.Trace?.Log( $"Killing process '{process.ProcessName}' (PID: {process.Id})." );

                process.Kill();
                process.WaitForExit();

                return true;
            }
            catch ( InvalidOperationException ) when ( process.HasExited )
            {
                // Nothing to do. We lost a race that ended the process.   
                return true;
            }
            catch ( Exception e )
            {
                this._logger.Error?.Log( $"Could not kill process '{process.ProcessName}' (PID: {process.Id}): {e.Message}." );
                errorMessage = e.Message;

                return false;
            }
        }

        public KillableProcess( Process process, ILogger logger, string? mainModule, KillableProcessSpec spec )
        {
            this.Process = process;
            this._logger = logger;
            this.Spec = spec;
            this._mainModule = mainModule;
        }

        public void Dispose() => this.Process.Dispose();

        /// <summary>
        /// Asks the process to shut down when its specification allows it, and kills it otherwise or when it does not exit.
        /// </summary>
        /// <returns><c>true</c> when the process has exited, <c>false</c> when it could not be killed.</returns>
        public bool ShutdownOrKill( out string? errorMessage )
        {
            if ( this.Spec.CanShutdown )
            {
                if ( this.Shutdown() )
                {
                    errorMessage = null;

                    return true;
                }
            }

            return this.Kill( out errorMessage );
        }
    }
}