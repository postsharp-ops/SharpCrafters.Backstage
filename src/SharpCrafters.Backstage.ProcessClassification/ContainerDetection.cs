// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Diagnostics;
using System.Runtime.InteropServices;

namespace SharpCrafters.Backstage.ProcessClassification;

/// <summary>
/// Determines whether the current process runs inside a container, without a service provider.
/// </summary>
/// <remarks>
/// A process that has started the Backstage services asks its <c>IContainerDetector</c> service instead, which caches
/// the answer and logs it through the logger of the process. That interface is declared in an assembly this one
/// cannot reference. This class exists for the processes that start no services, such as an MSBuild task, and for code
/// that runs before the services have started.
/// </remarks>
public static class ContainerDetection
{
    /// <summary>
    /// Determines whether the current process runs inside a container.
    /// </summary>
    /// <param name="logger">A logger that receives the reason for the answer, or <c>null</c>.</param>
    public static bool IsRunningInContainer( ILogger? logger = null )
    {
        if ( RuntimeInformation.IsOSPlatform( OSPlatform.Windows ) )
        {
            // The domain name is read only when the user name matches, because reading it can require a lookup of the
            // account that blocks when a domain controller is unreachable.
            if ( IsWindowsContainerAccount( Environment.UserName, () => Environment.UserDomainName ) )
            {
                logger?.Trace?.Log( $"Running inside a Windows container, detected from the account '{Environment.UserName}'." );

                return true;
            }

            logger?.Trace?.Log( "Not running inside a Windows container." );

            return false;
        }

        if ( !RuntimeInformation.IsOSPlatform( OSPlatform.Linux ) )
        {
            // There is no container on macOS: a container running on a Mac runs inside a Linux virtual machine,
            // and the process asking the question is then a Linux one.
            return false;
        }

        return IsLinuxContainer( "/", logger );
    }

    /// <summary>
    /// Determines whether the Linux system whose root directory is <paramref name="rootDirectory"/> is a container.
    /// </summary>
    /// <param name="rootDirectory">The root directory, which is <c>/</c> except in a test.</param>
    /// <param name="logger">A logger that receives the reason for the answer, or <c>null</c>.</param>
    internal static bool IsLinuxContainer( string rootDirectory, ILogger? logger )
    {
        string? ReadFileSafe( string path )
        {
            try
            {
                return File.ReadAllText( Path.Combine( rootDirectory, path ) );
            }
            catch ( Exception e )
            {
                logger?.Trace?.Log( $"Could not read '{path}' file: {e.Message}" );

                return null;
            }
        }

        // Docker creates /.dockerenv, and Podman creates /run/.containerenv, in every container that they start. These
        // files are the only signature of a container under control groups v2, where /proc/1/cgroup is '0::/' and so
        // names no container engine, when the first process of the container does not declare the container either.
        foreach ( var markerFile in new[] { ".dockerenv", "run/.containerenv" } )
        {
            if ( File.Exists( Path.Combine( rootDirectory, markerFile ) ) )
            {
                logger?.Trace?.Log( $"Running inside a container based on the file '/{markerFile}'." );

                return true;
            }
        }

        // If the process is running inside a Docker container
        // init (pid '1') process control group collection will have /docker/ as a part of the groups hierarchies.
        var process1ControlGroup = ReadFileSafe( "proc/1/cgroup" );

        if ( !string.IsNullOrEmpty( process1ControlGroup ) )
        {
            if ( process1ControlGroup!.IndexOf( "docker", StringComparison.Ordinal ) >= 0 )
            {
                logger?.Trace?.Log( "Running inside a Docker container based on process control group." );

                return true;
            }
        }

        var process1Environment = ReadFileSafe( "proc/1/environ" );

        if ( !string.IsNullOrEmpty( process1Environment ) )
        {
            if ( process1Environment!.IndexOf( "container=lxc", StringComparison.Ordinal ) >= 0 )
            {
                logger?.Trace?.Log( "Running inside a Docker container based on LXC container init process." );

                return true;
            }

            if ( process1Environment.IndexOf( "container=docker", StringComparison.Ordinal ) >= 0 )
            {
                logger?.Trace?.Log( "Running inside a Docker container based on Docker container init process." );

                return true;
            }

            // On Azure DevOps, the previous conditions aren't met, and we use the following condition instead.
            if ( process1Environment.IndexOf( "DOTNET_RUNNING_IN_CONTAINER=true", StringComparison.Ordinal ) >= 0 )
            {
                logger?.Trace?.Log( "Running inside a container based on sh dotnet startup process." );

                return true;
            }
        }

        logger?.Trace?.Log( "Not running inside a Docker container." );

        return false;
    }

    /// <summary>
    /// Determines whether an account is one of those a Windows container runs its processes as. That is the
    /// signature Microsoft documents for the case, and there is nothing else to look at: a Windows container has
    /// the same file system layout as a Windows installation.
    /// </summary>
    /// <param name="userName">The value of <see cref="Environment.UserName"/>.</param>
    /// <param name="getUserDomainName">
    /// Returns the value of <see cref="Environment.UserDomainName"/>. It is called only when
    /// <paramref name="userName"/> is the name of a container account.
    /// </param>
    /// <remarks>
    /// The two values are parameters rather than read here, so that a test can state the rule without running
    /// inside a container.
    /// </remarks>
    internal static bool IsWindowsContainerAccount( string userName, Func<string> getUserDomainName )
        => (StringComparer.OrdinalIgnoreCase.Equals( userName, "ContainerUser" )
            || StringComparer.OrdinalIgnoreCase.Equals( userName, "ContainerAdministrator" ))
           && StringComparer.OrdinalIgnoreCase.Equals( getUserDomainName(), "User Manager" );
}
