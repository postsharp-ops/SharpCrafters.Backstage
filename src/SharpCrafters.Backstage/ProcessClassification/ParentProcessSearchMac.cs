// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Utilities;
using System;
using System.Diagnostics;
using System.Globalization;

namespace SharpCrafters.Backstage.ProcessClassification;

internal sealed class ParentProcessSearchMac : ParentProcessSearch<int>
{
    public ParentProcessSearchMac( IServiceProvider serviceProvider ) : base( serviceProvider ) { }

    protected override bool IsNull( int handle ) => handle == 0;

    protected override int GetCurrentProcessHandle() => Process.GetCurrentProcess().Id;

    protected override (string? ImageName, int CurrentProcessId, int ParentProcessHandle) GetProcessInfo( int processHandle )
    {
        // There's no handle on Mac.
        var processId = processHandle;

        try
        {
            var processStartInfo = new ProcessStartInfo
            {
                FileName = "ps",

                // On macOS, the comm column is the path of the executable, whereas the command column is the command line,
                // whose first word is not the path when the path contains a space.
                Arguments = $"-o ppid= -o comm= -p {processId}",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using ( var cmdProcess = Process.Start( processStartInfo ) )
            {
                if ( cmdProcess == null )
                {
                    throw new InvalidOperationException( "Failed to start 'ps' command." );
                }

                var output = cmdProcess.StandardOutput.ReadToEnd().Trim();
                cmdProcess.WaitForExit();

                this.Logger.Trace?.Log( $"ps {processId} output: {output}" );

                return (GetImageName( output, out var parentProcessId ), processId, parentProcessId);
            }
        }
        catch ( Exception ex )
        {
            Console.WriteLine( "Error reading parent process on macOS: " + ex.Message );

            throw;
        }
    }

    /// <summary>
    /// Gets the name of the executable and the parent process identifier from the output of <c>ps -o ppid= -o comm=</c>,
    /// which is the identifier, spaces, and the path of the executable, for instance <c>1 /usr/local/bin/dotnet</c> or
    /// <c>512 -zsh</c>. The path can contain spaces.
    /// </summary>
    internal static string GetImageName( string output, out int parentProcessId )
    {
        var trimmedOutput = output.Trim();
        var separator = trimmedOutput.IndexOfOrdinal( ' ' );

        if ( separator < 0 )
        {
            throw new InvalidOperationException( $"Unexpected output from 'ps' command: '{output}'." );
        }

        parentProcessId = int.Parse( trimmedOutput.Substring( 0, separator ), CultureInfo.InvariantCulture );

        var path = trimmedOutput.Substring( separator + 1 ).Trim();

        return path.Substring( path.LastIndexOf( '/' ) + 1 );
    }

    protected override void CloseProcessHandle( int handle ) { }
}