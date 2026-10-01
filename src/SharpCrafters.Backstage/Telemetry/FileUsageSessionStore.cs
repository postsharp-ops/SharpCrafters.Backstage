// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using JetBrains.Annotations;
using SharpCrafters.Backstage.Extensibility;
using SharpCrafters.Backstage.Infrastructure;
using SharpCrafters.Backstage.Utilities;
using System;
using System.Globalization;
using System.IO;

namespace SharpCrafters.Backstage.Telemetry;

/// <summary>
/// An <see cref="IUsageSessionStore"/> that keeps one file per project in
/// <see cref="IStandardDirectories.TelemetrySessionsDirectory"/>.
/// </summary>
/// <remarks>
/// <para>
/// The name of a file is a hash of the upper-case project key, because a project name may contain characters that a
/// file name cannot, and because the key is compared without case. The first line of the file is the time of the last
/// report in the round-trip format, and the second line is the project key, which only serves a human reading the
/// directory. The time is stored in the content rather than in the modification time of the file, so that it comes from
/// <see cref="IDateTimeProvider"/>.
/// </para>
/// <para>
/// A file is written with <see cref="IFileSystem.WriteAllTextAtomically"/>, so a reader without the lock never sees a
/// partially written file. A file that cannot be parsed counts as no record.
/// </para>
/// <para>
/// The store deletes nothing. The directory is under <see cref="IStandardDirectories.TelemetryDirectory"/>, from which
/// the maintenance pass deletes the files older than the telemetry retention period. A project that is still built
/// rewrites its file once per period, so its file is never old enough to be deleted.
/// </para>
/// </remarks>
[PublicAPI]
public sealed class FileUsageSessionStore : UsageSessionStore
{
    private readonly IFileSystem _fileSystem;
    private readonly string _directory;

    /// <summary>
    /// Initializes a new instance of the <see cref="FileUsageSessionStore"/> class.
    /// </summary>
    public FileUsageSessionStore( IServiceProvider serviceProvider ) : base( serviceProvider )
    {
        this._fileSystem = serviceProvider.GetRequiredBackstageService<IFileSystem>();
        this._directory = serviceProvider.GetRequiredBackstageService<IStandardDirectories>().TelemetrySessionsDirectory;
    }

    internal string GetFilePath( string projectKey )
        => Path.Combine( this._directory, HashUtilities.HashToString( projectKey.ToUpperInvariant() ) + ".session" );

    /// <inheritdoc />
    protected override string GetLockResourceName( string projectKey ) => this.GetFilePath( projectKey );

    /// <inheritdoc />
    protected override DateTime? ReadLastReportTime( string projectKey )
    {
        var path = this.GetFilePath( projectKey );

        if ( !this._fileSystem.FileExists( path ) )
        {
            return null;
        }

        string content;

        try
        {
            content = this._fileSystem.ReadAllText( path );
        }
        catch ( Exception e ) when ( e is IOException or UnauthorizedAccessException )
        {
            // The file was deleted by the maintenance pass, or is being substituted by a writer, between the two calls.
            this.Logger.Trace?.Log( $"Cannot read '{path}': {e.Message}" );

            return null;
        }

        var endOfLine = content.IndexOfAny( ['\r', '\n'] );
        var firstLine = endOfLine < 0 ? content : content.Substring( 0, endOfLine );

        if ( DateTime.TryParse( firstLine, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var time ) )
        {
            return time.ToUniversalTime();
        }

        this.Logger.Warning?.Log( $"The content of '{path}' is not a valid session record. It is ignored." );

        return null;
    }

    /// <inheritdoc />
    protected override void WriteLastReportTime( string projectKey, DateTime time )
    {
        if ( !this._fileSystem.DirectoryExists( this._directory ) )
        {
            this._fileSystem.CreateDirectory( this._directory );
        }

        var content = time.ToUniversalTime().ToString( "O", CultureInfo.InvariantCulture ) + Environment.NewLine + projectKey + Environment.NewLine;

        this._fileSystem.WriteAllTextAtomically( this.GetFilePath( projectKey ), content );
    }
}
