// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Application;
using SharpCrafters.Backstage.Configuration;
using SharpCrafters.Backstage.Extensibility;
using SharpCrafters.Backstage.Infrastructure;
using SharpCrafters.Backstage.Maintenance;
using SharpCrafters.Backstage.ProcessClassification;
using SharpCrafters.Backstage.Threading;
using SharpCrafters.Backstage.Utilities;
#if !NETFRAMEWORK
using Microsoft.Diagnostics.NETCore.Client;
using System.Runtime.InteropServices;
#endif
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;

namespace SharpCrafters.Backstage.Diagnostics;

internal sealed class MiniDumper : IMiniDumper, IDisposable
{
    private static readonly object _dumpersLock = new();
    private static int _isAppDomainInitialized;

    // The live dumpers of the process, in the order of their creation. Several service providers can coexist in one
    // process, for instance the providers of the products of a composite product, and each one creates its own dumper.
    // The array is replaced, never modified, so that the first-chance handler reads it without taking the lock.
    private static volatile WeakReference<MiniDumper>[] _dumpers = [];

    // The number of dumps being written in the process, by any instance. While it is not zero, the first-chance handler
    // does nothing: an exception thrown while a dump is written, on the writing thread or on a thread of the diagnostics
    // client, would otherwise start another dump, recursively on the same thread, and under the MiniDump lock.
    private static int _writesInProgress;

    private readonly ILogger _logger;
    private readonly CrashDumpConfiguration _configuration;
    private readonly bool _isProcessEnabled;
    private readonly ProcessKind _processKind;
    private readonly ITempFileManager _tempFileManager;
    private readonly IPlatformInfo _platformInfo;
    private readonly INamedLockService _lockService;

    public MiniDumper( IServiceProvider serviceProvider )
    {
        this._lockService = serviceProvider.GetRequiredBackstageService<INamedLockService>();
        this._logger = serviceProvider.GetLoggerFactory().GetLogger( "Dumper" );
        this._configuration = serviceProvider.GetRequiredBackstageService<IConfigurationManager>().Get<DiagnosticsConfiguration>().CrashDumps;
        this._tempFileManager = serviceProvider.GetRequiredBackstageService<ITempFileManager>();
        this._platformInfo = serviceProvider.GetRequiredBackstageService<IPlatformInfo>();

        var applicationInfo = serviceProvider.GetRequiredBackstageService<IApplicationInfoProvider>();
        this._processKind = applicationInfo.ProcessKind;
        this._isProcessEnabled = this._configuration.Processes.TryGetValue( this._processKind.ToString(), out var isEnabled ) && isEnabled;

        // The MiniDumper class is instantiated for each project, but the handler must be global.
        // We will use the latest available configuration in the process.
        if ( this._isProcessEnabled )
        {
            if ( Interlocked.Exchange( ref _isAppDomainInitialized, 1 ) == 0 )
            {
                AppDomain.CurrentDomain.FirstChanceException += OnFirstChanceException;
            }
        }

        lock ( _dumpersLock )
        {
            _dumpers = [.. GetLiveDumpers( _dumpers ), new WeakReference<MiniDumper>( this )];
        }
    }

    /// <summary>
    /// Removes the dumper from the dumpers that the first-chance handler considers.
    /// </summary>
    public void Dispose()
    {
        lock ( _dumpersLock )
        {
            _dumpers = GetLiveDumpers( _dumpers ).Where( r => r.TryGetTarget( out var d ) && d != this ).ToArray();
        }
    }

    private static IEnumerable<WeakReference<MiniDumper>> GetLiveDumpers( WeakReference<MiniDumper>[] dumpers )
        => dumpers.Where( r => r.TryGetTarget( out _ ) );

    private static void OnFirstChanceException( object? sender, FirstChanceExceptionEventArgs e )
    {
        if ( Volatile.Read( ref _writesInProgress ) != 0 )
        {
            return;
        }

        var dumpers = _dumpers;

        // The latest dumper is asked first, because it has read the latest configuration. One dump is enough for one
        // exception, so the first dumper that accepts the exception writes it and the others are not asked.
        for ( var i = dumpers.Length - 1; i >= 0; i-- )
        {
            if ( dumpers[i].TryGetTarget( out var dumper ) && dumper.MustWrite( e.Exception ) )
            {
                dumper.Write();

                return;
            }
        }
    }

    public bool MustWrite( Exception exception )
        => this._isProcessEnabled
           && exception is not (TaskCanceledException or OperationCanceledException or IOException or WebException)
           && !(exception is AggregateException { InnerExceptions.Count: 1 } aggregateException
                && !this.MustWrite( aggregateException.InnerException! ))
           && (this._configuration.ExceptionTypes.Contains( exception.GetType().Name ) || this._configuration.ExceptionTypes.Contains( "*" ));

    /// <summary>
    /// Writes a full dump of the current process.
    /// </summary>
    /// <remarks>
    /// <para>
    /// On the .NET runtime, the dump is requested from the runtime through its diagnostics channel
    /// (<c>DiagnosticsClient.WriteDump</c>), which is what <c>dotnet-dump collect</c> does, without the tool: the runtime starts
    /// <c>createdump</c> as its own child and lets it read its memory. No global tool has to be installed, and no capability
    /// is needed in a Linux container, where a <c>createdump</c> started by the process itself could not trace its parent. It
    /// needs the diagnostics of the runtime, which <c>DOTNET_EnableDiagnostics=0</c> turns off.
    /// </para>
    /// <para>
    /// A .NET Framework process has no diagnostics channel, and <c>dotnet dump collect</c> is still invoked there.
    /// </para>
    /// </remarks>
    private bool WriteDump( string fileName )
    {
#if !NETFRAMEWORK
        if ( !RuntimeInformation.FrameworkDescription.StartsWith( ".NET Framework", StringComparison.Ordinal ) )
        {
            new DiagnosticsClient( Process.GetCurrentProcess().Id ).WriteDump( DumpType.Full, fileName, logDumpGeneration: false );

            return File.Exists( fileName );
        }
#endif

        return ToolInvocationHelper.InvokeTool(
            this._logger,
            this._platformInfo.DotNetExePath,
            $"dump collect -p {Process.GetCurrentProcess().Id} -o \"{fileName}\"",
            null );
    }

    public string? Write( MiniDumpOptions? options = null )
    {
        Interlocked.Increment( ref _writesInProgress );

        try
        {
            return this.WriteCore( options ?? MiniDumpOptions.Default );
        }
        finally
        {
            Interlocked.Decrement( ref _writesInProgress );
        }
    }

    private string? WriteCore( MiniDumpOptions options )
    {

        string directory;

        try
        {
            // Before the lock below: ITempFileManager.GetTempDirectory takes a named lock of its own, and the lock
            // service rejects a named lock acquired while another one is held.
            directory = this._tempFileManager.GetTempDirectory( "CrashReports", CleanUpStrategy.Always );
        }
        catch ( Exception e )
        {
            this._logger.LogException( e );

            return null;
        }

        using ( this._lockService.WithGlobalLock( "MiniDump" ) )
        {
            try
            {
                var fileName = Path.Combine( directory, $"{this._processKind.ToString().ToLowerInvariant()}-{Guid.NewGuid()}.dmp" );

                this._logger.Info?.Log( $"Saving a dump to '{fileName}.'" );

                if ( !this.WriteDump( fileName ) )
                {
                    return null;
                }

                if ( !options.Compress )
                {
                    return fileName;
                }
                else
                {
                    var compressedFileName = fileName + ".gz";

                    this._logger.Info?.Log( $"Compressing dump to '{compressedFileName}.'" );

                    using ( var readStream = File.OpenRead( fileName ) )
                    using ( var writeStream = new GZipStream( File.OpenWrite( compressedFileName ), CompressionMode.Compress ) )
                    {
                        readStream.CopyTo( writeStream );
                    }

                    File.Delete( fileName );

                    return compressedFileName;
                }
            }
            catch ( Exception e )
            {
                this._logger.LogException( e );

                return null;
            }
        }
    }
}