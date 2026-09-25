// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Diagnostics;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace SharpCrafters.Backstage.ProcessClassification;

internal abstract class ParentProcessSearch : IParentProcessSearch
{
    /// <inheritdoc />
    public abstract IReadOnlyList<ProcessInfo> GetParentProcesses( ISet<string>? pivots = null );

    protected ILogger Logger { get; }

    protected ParentProcessSearch( IServiceProvider serviceProvider )
    {
        this.Logger = serviceProvider.GetLoggerFactory().GetLogger( nameof(ParentProcessSearch) );
    }

    public static ParentProcessSearch Create( IServiceProvider serviceProvider )
    {
        if ( RuntimeInformation.IsOSPlatform( OSPlatform.Windows ) )
        {
            return new ParentProcessSearchWindows( serviceProvider );
        }
        else if ( RuntimeInformation.IsOSPlatform( OSPlatform.Linux ) )
        {
            return new ParentProcessSearchLinux( serviceProvider );
        }
        else if ( RuntimeInformation.IsOSPlatform( OSPlatform.OSX ) )
        {
            return new ParentProcessSearchMac( serviceProvider );
        }
        else
        {
            throw new NotSupportedException( $"{RuntimeInformation.OSDescription} is not supported." );
        }
    }
}