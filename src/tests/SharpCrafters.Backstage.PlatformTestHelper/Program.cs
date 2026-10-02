// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Extensibility;
using SharpCrafters.Backstage.PlatformTests;
using SharpCrafters.Backstage.ProcessClassification;
using SharpCrafters.Backstage.Threading;
using System.Diagnostics;
using System.Reflection;

// The shutdown request that IProcessManager sends to a process that it takes for a Backstage tool. The helper has nothing
// to shut down, so it acknowledges the request, and the process manager then kills the process.
if ( args.Contains( "-shutdown" ) )
{
    return 0;
}

switch ( args )
{
    case [HelperCommands.HoldLock, var name]:
        {
            using var namedLock = PlatformTestServices.CreateServiceProvider().GetRequiredBackstageService<INamedLockService>().GetLock( name );

            using ( namedLock.Acquire() )
            {
                Console.WriteLine( HelperCommands.AcquiredLine );
                Console.ReadLine();
            }

            return 0;
        }

    case [HelperCommands.AbandonLock, var name]:
        {
            var namedLock = PlatformTestServices.CreateServiceProvider().GetRequiredBackstageService<INamedLockService>().GetLock( name );
            namedLock.Acquire();
            Console.WriteLine( HelperCommands.AcquiredLine );

            // The lock is never released: the test kills this process while it holds the lock.
            Thread.Sleep( Timeout.Infinite );

            return 0;
        }

    case [HelperCommands.PrintParents]:
        {
            var parentProcessSearch = PlatformTestServices.CreateServiceProvider().GetRequiredBackstageService<IParentProcessSearch>();

            foreach ( var parent in parentProcessSearch.GetParentProcesses() )
            {
                Console.WriteLine( $"{parent.ProcessId} {parent.ProcessName}" );
            }

            Console.WriteLine( HelperCommands.EndLine );

            return 0;
        }

    case [HelperCommands.Spawn, .. var childArguments]:
        {
            var startInfo = new ProcessStartInfo( DotNetHost.ExecutablePath ) { RedirectStandardOutput = true, UseShellExecute = false };
            startInfo.ArgumentList.Add( Assembly.GetEntryAssembly()!.Location );

            foreach ( var argument in childArguments )
            {
                startInfo.ArgumentList.Add( argument );
            }

            using var child = Process.Start( startInfo )!;

            while ( child.StandardOutput.ReadLine() is { } line )
            {
                Console.WriteLine( line );
            }

            child.WaitForExit();

            return child.ExitCode;
        }

    case [HelperCommands.Wait]:
        Console.WriteLine( HelperCommands.ReadyLine );
        Console.ReadLine();

        return 0;

    default:
        Console.Error.WriteLine( $"Unknown command: '{string.Join( " ", args )}'." );

        return 1;
}
