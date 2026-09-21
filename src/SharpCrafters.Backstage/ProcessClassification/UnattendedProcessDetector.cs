// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Application;
using SharpCrafters.Backstage.Diagnostics;
using SharpCrafters.Backstage.Extensibility;
using SharpCrafters.Backstage.Infrastructure;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;

namespace SharpCrafters.Backstage.ProcessClassification;

internal class UnattendedProcessDetector : IUnattendedProcessDetector
{
    /// <summary>
    /// The environment variable, after the prefix of the product, that forces the attended answer. It is
    /// <c>METALAMA_FORCE_ATTENDED</c> for Metalama.
    /// </summary>
    public const string ForceAttendedVariableName = "FORCE_ATTENDED";

    private readonly ILogger _logger;
    private readonly Lazy<bool> _isUnattendedLazy;
    private readonly IContainerDetector _containerDetector;
    private readonly ProductProfile _productProfile;
    private readonly ParentProcessSearch _parentProcessSearch;

    public UnattendedProcessDetector( IServiceProvider serviceProvider )
    {
        this._logger = serviceProvider.GetLoggerFactory().GetLogger( nameof(UnattendedProcessDetector) );
        this._isUnattendedLazy = new Lazy<bool>( this.IsCurrentProcessUnattendedCore );
        this._containerDetector = serviceProvider.GetRequiredBackstageService<IContainerDetector>();
        this._productProfile = serviceProvider.GetRequiredBackstageService<ProductProfile>();
        this._parentProcessSearch = serviceProvider.GetRequiredBackstageService<ParentProcessSearch>();
    }

    public bool IsCurrentProcessUnattended => this._isUnattendedLazy.Value;

    private bool IsCurrentProcessUnattendedCore()
    {
        var variableName = this._productProfile.GetEnvironmentVariableName( ForceAttendedVariableName );

        if ( bool.TryParse( Environment.GetEnvironmentVariable( variableName ), out var forceAttended ) && forceAttended )
        {
            this._logger.Trace?.Log( $"Attended mode forced by the '{variableName}' environment variable." );

            return false;
        }

        if ( !Environment.UserInteractive )
        {
            this._logger.Trace?.Log( "Unattended mode detected because Environment.UserInteractive = false." );

            return true;
        }

        if ( this._containerDetector.IsRunningInContainer )
        {
            this._logger.Trace?.Log( "Unattended mode detected because of a containerized environment." );

            return true;
        }

        if ( RuntimeInformation.IsOSPlatform( OSPlatform.Windows ) )
        {
            if ( Environment.OSVersion.Version.Major >= 6 && Process.GetCurrentProcess().SessionId == 0 )
            {
                this._logger.Trace?.Log( "Unattended mode detected because SessionId = 0 on Windows." );

                return true;
            }
        }

        // Processes that have no user interface and are not the agent of a continuous integration server. A
        // continuous integration server is recognized by ContinuousIntegrationDetector below, which requires an
        // environment variable in addition to the process.
        var unattendedProcesses = new HashSet<string>( StringComparer.OrdinalIgnoreCase ) { "services" };

        var notUnattendedProcesses = new HashSet<string>( StringComparer.OrdinalIgnoreCase )
        {
            "rider" // Rider needs to be checked, because it can have Java as its parent process.
        };

        // The search of the parent processes stops at one of these processes, so that it does not walk the chain
        // further than necessary.
        var pivots = new HashSet<string>( unattendedProcesses, StringComparer.OrdinalIgnoreCase );
        pivots.UnionWith( ContinuousIntegrationDetector.AgentProcessNames );

        IReadOnlyList<ProcessInfo> parentProcesses;

        try
        {
            parentProcesses = this._parentProcessSearch.GetParentProcesses( pivots );
        }
        catch ( Exception e )
        {
            if ( RuntimeInformation.IsOSPlatform( OSPlatform.Linux ) || RuntimeInformation.IsOSPlatform( OSPlatform.OSX ) )
            {
                this._logger.Warning?.Log( $"Unattended mode detected because the detection was not successful: {e}" );

                return true;
            }
            else
            {
                this._logger.Error?.Log( $"Unattended mode detected because the detection was not successful: {e}" );

                return false;
            }
        }

        if ( this._logger.Trace != null )
        {
            this._logger.Trace?.Log( "Parent processes:" );

            foreach ( var process in parentProcesses )
            {
                this._logger.Trace?.Log(
                    process.ImagePath == null ? $"- Unknown process ID {process.ProcessId}" : $"- {process.ProcessName}: {process.ImagePath}" );
            }
        }

        var parentProcessNames = parentProcesses.Where( p => p.ProcessName != null ).Select( p => p.ProcessName! ).ToArray();

        var notUnattendedProcessName = parentProcessNames.FirstOrDefault( notUnattendedProcesses.Contains );

        if ( notUnattendedProcessName != null )
        {
            this._logger.Trace?.Log( $"Unattended mode NOT detected because of parent process '{notUnattendedProcessName}'." );

            return false;
        }

        var unattendedProcessName = parentProcessNames.FirstOrDefault( unattendedProcesses.Contains );

        if ( unattendedProcessName != null )
        {
            this._logger.Trace?.Log( $"Unattended mode detected because of parent process '{unattendedProcessName}'." );

            return true;
        }

        var continuousIntegrationContext = new ContinuousIntegrationContext( new EnvironmentVariableProvider(), () => parentProcessNames, this._logger );
        var continuousIntegrationServerName = ContinuousIntegrationDetector.GetServerName( continuousIntegrationContext );

        if ( continuousIntegrationServerName != null )
        {
            this._logger.Trace?.Log( $"Unattended mode detected because the current process runs on {continuousIntegrationServerName}." );

            return true;
        }

        this._logger.Trace?.Log( "Unattended mode NOT detected." );

        return false;
    }
}