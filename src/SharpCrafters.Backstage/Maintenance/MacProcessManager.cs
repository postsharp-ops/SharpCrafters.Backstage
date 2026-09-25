// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace SharpCrafters.Backstage.Maintenance;

/// <summary>
/// The implementation of <see cref="IProcessManager"/> for macOS.
/// </summary>
internal sealed class MacProcessManager : ProcessManagerBase
{
    public MacProcessManager( IServiceProvider serviceProvider ) : base( serviceProvider ) { }

    protected override bool TryGetModulePaths( Process process, [NotNullWhen( true )] out List<string>? modules )
    {
        modules = [];

        using var listOpenFilesProcess = new Process()
        {
            StartInfo = new ProcessStartInfo() { FileName = "lsof", Arguments = $"-p {process.Id}", RedirectStandardOutput = true }
        };

        listOpenFilesProcess.Start();

        // The output is read to its end before waiting for the exit. lsof lists every file that a .NET process maps, which
        // exceeds the buffer of the pipe, and lsof then blocks until the output is read.
#pragma warning disable CA1307
        while ( listOpenFilesProcess.StandardOutput.ReadLine() is { } outputLine )
        {
            var module = outputLine.Split( ' ' ).LastOrDefault();

            if ( module != null )
            {
                modules.Add( module );
            }
        }

        listOpenFilesProcess.WaitForExit();

        return true;
    }
}