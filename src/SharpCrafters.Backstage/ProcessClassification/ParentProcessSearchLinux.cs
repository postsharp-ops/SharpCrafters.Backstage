// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
#if !NET
using System.Runtime.InteropServices;
using System.Text;
#endif

namespace SharpCrafters.Backstage.ProcessClassification;

internal sealed class ParentProcessSearchLinux : ParentProcessSearch<int>
{
    /// <summary>
    /// The number of characters that Linux keeps of the name of a process in <c>/proc/&lt;pid&gt;/comm</c>.
    /// </summary>
    private const int _maxCommandNameLength = 15;

    public ParentProcessSearchLinux( IServiceProvider serviceProvider ) : base( serviceProvider ) { }

    protected override bool IsNull( int handle ) => handle == 0;

    protected override int GetCurrentProcessHandle() => Process.GetCurrentProcess().Id;

    protected override (string? ImageName, int CurrentProcessId, int ParentProcessHandle) GetProcessInfo( int processHandle )
    {
        // There's no handle on Linux.
        var processId = processHandle;
        string? commandImageName;

        try
        {
            commandImageName = File.ReadAllText( "/proc/" + processId + "/comm" ).Trim();
        }
        catch ( Exception e )
        {
            this.Logger.Error?.Log( $"Could not read '/proc/{processId}/comm' file: {e.Message}" );
            commandImageName = null;
        }

        if ( commandImageName is { Length: _maxCommandNameLength } )
        {
            commandImageName = GetUntruncatedName( commandImageName, this.TryReadExecutablePath( processId ) );
        }

        // Read status file of the process.
        string? processStatus;

        try
        {
            processStatus = File.ReadAllText( "/proc/" + processId + "/stat" );
        }
        catch ( Exception e )
        {
            this.Logger.Error?.Log( $"Could not read '/proc/{processId}/stat' file: {e.Message}" );

            throw;
        }

        int parentProcessId;

        try
        {
            parentProcessId = GetParentProcessId( processStatus );
        }
        catch ( Exception e )
        {
            this.Logger.Error?.Log( $"Could not parse PPID from process '{processId}' status file: {e.Message}" );

            throw;
        }

        return (commandImageName, processId, parentProcessId);
    }

    /// <summary>
    /// Gets the parent process identifier from the content of <c>/proc/&lt;pid&gt;/stat</c>.
    /// </summary>
    /// <remarks>
    /// The second field is the name of the process between parentheses, and the name can itself contain spaces and
    /// parentheses. The fields that follow it are therefore counted from the last closing parenthesis. The first of them
    /// is the state of the process, and the second is the parent process identifier.
    /// </remarks>
    internal static int GetParentProcessId( string processStatus )
    {
        var nameEnd = processStatus.LastIndexOf( ')' );

        if ( nameEnd < 0 )
        {
            throw new FormatException( "The status of the process has no name between parentheses." );
        }

        var fields = processStatus.Substring( nameEnd + 1 ).Split( new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries );

        if ( fields.Length < 2 )
        {
            throw new FormatException( "The status of the process has no parent process identifier." );
        }

        return int.Parse( fields[1], CultureInfo.InvariantCulture );
    }

    /// <summary>
    /// Gets the name of a process whose name in <c>/proc/&lt;pid&gt;/comm</c> may have been truncated.
    /// </summary>
    /// <param name="commandName">The name in <c>/proc/&lt;pid&gt;/comm</c>.</param>
    /// <param name="executablePath">The target of <c>/proc/&lt;pid&gt;/exe</c>, or <c>null</c> when it cannot be read.</param>
    /// <remarks>
    /// The file name of the executable is the full name only when it begins with the truncated name. Otherwise, the name
    /// was set by other means, for instance by a script whose interpreter is the executable, and it is kept.
    /// </remarks>
    internal static string GetUntruncatedName( string commandName, string? executablePath )
    {
        if ( executablePath == null )
        {
            return commandName;
        }

        var executableName = Path.GetFileName( executablePath );

        return executableName.Length > commandName.Length && executableName.StartsWith( commandName, StringComparison.Ordinal )
            ? executableName
            : commandName;
    }

    private string? TryReadExecutablePath( int processId )
    {
        var path = $"/proc/{processId}/exe";

        try
        {
#if NET
            return new FileInfo( path ).LinkTarget;
#else
            var buffer = new byte[4096];
            var length = (long) ReadLink( path, buffer, (IntPtr) buffer.Length );

            return length > 0 ? Encoding.UTF8.GetString( buffer, 0, (int) length ) : null;
#endif
        }
        catch ( Exception e )
        {
            // The link of a process of another user cannot be read. The truncated name is then the only one available.
            this.Logger.Trace?.Log( $"Could not read '{path}': {e.Message}" );

            return null;
        }
    }

#if !NET
    [DllImport( "libc", EntryPoint = "readlink", SetLastError = true )]
    private static extern IntPtr ReadLink( string path, byte[] buffer, IntPtr bufferSize );
#endif

    protected override void CloseProcessHandle( int handle ) { }
}
