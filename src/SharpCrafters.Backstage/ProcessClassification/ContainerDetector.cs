// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Diagnostics;
using SharpCrafters.Backstage.Utilities;
using System;
using System.IO;
using System.Runtime.InteropServices;

namespace SharpCrafters.Backstage.ProcessClassification;

internal class ContainerDetector : IContainerDetector
{
    private readonly ILogger _logger;
    private readonly Lazy<bool> _isRunningInContainerLazy;

    public ContainerDetector( IServiceProvider serviceProvider )
    {
        this._logger = serviceProvider.GetLoggerFactory().GetLogger( nameof(ContainerDetector) );
        this._isRunningInContainerLazy = new Lazy<bool>( this.IsRunningInContainerCore );
    }

    public bool IsRunningInContainer => this._isRunningInContainerLazy.Value;

    /// <summary>
    /// Determines whether an account is one of those a Windows container runs its processes as. That is the
    /// signature Microsoft documents for the case, and there is nothing else to look at: a Windows container has
    /// the same file system layout as a Windows installation.
    /// </summary>
    /// <param name="userName">The value of <see cref="Environment.UserName"/>.</param>
    /// <param name="userDomainName">The value of <see cref="Environment.UserDomainName"/>.</param>
    /// <remarks>
    /// The two values are parameters rather than read here, so that a test can state the rule without running
    /// inside a container.
    /// </remarks>
    internal static bool IsWindowsContainerAccount( string userName, string userDomainName )
        => (StringComparer.OrdinalIgnoreCase.Equals( userName, "ContainerUser" )
            || StringComparer.OrdinalIgnoreCase.Equals( userName, "ContainerAdministrator" ))
           && StringComparer.OrdinalIgnoreCase.Equals( userDomainName, "User Manager" );

    private bool IsRunningInContainerCore()
    {
        if ( RuntimeInformation.IsOSPlatform( OSPlatform.Windows ) )
        {
            if ( IsWindowsContainerAccount( Environment.UserName, Environment.UserDomainName ) )
            {
                this._logger.Trace?.Log( $"Running inside a Windows container, detected from the account '{Environment.UserName}'." );

                return true;
            }

            this._logger.Trace?.Log( "Not running inside a Windows container." );

            return false;
        }

        if ( !RuntimeInformation.IsOSPlatform( OSPlatform.Linux ) )
        {
            // There is no container on macOS: a container running on a Mac runs inside a Linux virtual machine,
            // and the process asking the question is then a Linux one.
            return false;
        }

        string? ReadFileSafe( string path )
        {
            try
            {
                return File.ReadAllText( path );
            }
            catch ( Exception e )
            {
                this._logger.Trace?.Log( $"Could not read '{path}' file: {e.Message}" );

                return null;
            }
        }

        // If the process is running inside a Docker container
        // init (pid '1') process control group collection will have /docker/ as a part of the groups hierarchies.
        var process1ControlGroup = ReadFileSafe( "/proc/1/cgroup" );

        if ( !string.IsNullOrEmpty( process1ControlGroup ) )
        {
            if ( process1ControlGroup.ContainsOrdinal( "docker" ) )
            {
                this._logger.Trace?.Log( "Running inside a Docker container based on process control group." );

                return true;
            }
        }

        var process1Environment = ReadFileSafe( "/proc/1/environ" );

        if ( !string.IsNullOrEmpty( process1Environment ) )
        {
            if ( process1Environment.ContainsOrdinal( "container=lxc" ) )
            {
                this._logger.Trace?.Log( "Running inside a Docker container based on LXC container init process." );

                return true;
            }

            if ( process1Environment.ContainsOrdinal( "container=docker" ) )
            {
                this._logger.Trace?.Log( "Running inside a Docker container based on Docker container init process." );

                return true;
            }

            // On Azure DevOps, the previous conditions aren't met, and we use the following condition instead.
            if ( process1Environment.ContainsOrdinal( "DOTNET_RUNNING_IN_CONTAINER=true" ) )
            {
                this._logger.Trace?.Log( "Running inside a container based on sh dotnet startup process." );

                return true;
            }
        }

        this._logger.Trace?.Log( "Not running inside a Docker container." );

        return false;
    }
}