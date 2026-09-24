// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

// There is a copy of this code in Metalama.Compiler.Shared and partially in Metalama ResourceExtractor.

using JetBrains.Annotations;
using SharpCrafters.Backstage.Extensibility;
using SharpCrafters.Backstage.Infrastructure;
using SharpCrafters.Backstage.Utilities;
using System;
using System.IO;

namespace Metalama.Backstage.Utilities;

[PublicAPI]
public static class MetalamaPathUtilities
{
    private static readonly string? _overriddenTempPath;

    static MetalamaPathUtilities()
    {
        var overriddenTempPath = Environment.GetEnvironmentVariable( "METALAMA_TEMP" );
        _overriddenTempPath = string.IsNullOrEmpty( overriddenTempPath ) ? null : overriddenTempPath;
    }

    [Obsolete( "Use GetTempDirectory() or IStandardDirectories.TempDirectory. GetTempPath() is rooted in the world-writable /tmp on Unix (issue #1650)." )]
    public static string GetTempPath() => _overriddenTempPath ?? Path.GetTempPath();

    /// <summary>
    /// Gets the Metalama temporary directory from the process-wide service provider. The actual logic lives in
    /// <see cref="IStandardDirectories.TempDirectory" />.
    /// </summary>
    [Obsolete( "Use GetTempDirectory(IServiceProvider) or IStandardDirectories.TempDirectory. The process-wide service provider is obsolete." )]
    public static string GetTempDirectory()
        => BackstageServiceFactory.ServiceProvider.GetRequiredBackstageService<IStandardDirectories>().TempDirectory;

    /// <summary>
    /// Gets the Metalama temporary directory from the given service provider. The actual logic lives in
    /// <see cref="IStandardDirectories.TempDirectory" />.
    /// </summary>
    public static string GetTempDirectory( IServiceProvider serviceProvider )
        => serviceProvider.GetRequiredBackstageService<IStandardDirectories>().TempDirectory;

    [Obsolete( "Use GetTempFileName(string) with GetTempDirectory(IServiceProvider). The process-wide service provider is obsolete." )]
    public static string GetTempFileName() => GetTempFileName( GetTempDirectory() );

    /// <summary>
    /// Creates a uniquely-named, empty temporary file in the given <paramref name="directory" /> and returns its full path.
    /// Callers that have a service provider at hand should resolve <see cref="IStandardDirectories.TempDirectory" /> themselves
    /// and pass it here, rather than relying on the parameterless overload, which requires the backstage services to be initialized.
    /// </summary>
    public static string GetTempFileName( string directory ) => TempFileUtilities.GetTempFileName( directory );
}
