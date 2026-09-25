// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Extensibility;
using SharpCrafters.Backstage.Infrastructure;
using SharpCrafters.Backstage.Testing;
using Xunit;

namespace SharpCrafters.Backstage.PlatformTests.MachineEnvironment;

/// <summary>
/// Compares what the services read from the operating system with what the operating system holds. The unit tests give
/// the services a fake file system and a fake operating system, so they cannot tell whether the real locations exist.
/// </summary>
public sealed class MachineEnvironmentTests
{
    private readonly IServiceProvider _serviceProvider = PlatformTestServices.CreateServiceProvider();

    /// <summary>
    /// The application data directory is under the local application data directory of the operating system, which
    /// Linux and macOS provide, so that the fallback for the platforms without one is not taken. On Unix, the temporary
    /// directory is under the application data directory, because <c>/tmp</c> is writable by every user (issue #1650).
    /// </summary>
    /// <remarks>
    /// The home directory is not the reference. On macOS, the runtime resolves the local application data directory,
    /// <c>~/Library/Application Support</c>, from the account of the user, whereas the user profile follows the
    /// <c>HOME</c> variable, which the macOS runner of these tests redirects.
    /// </remarks>
    [PlatformFact( TestPlatforms.Unix )]
    public void TheDataDirectoriesAreInTheLocalApplicationDataOfTheUser()
    {
        var directories = this._serviceProvider.GetRequiredBackstageService<IStandardDirectories>();
        var localApplicationData = Environment.GetFolderPath( Environment.SpecialFolder.LocalApplicationData );

        Assert.False( string.IsNullOrEmpty( localApplicationData ) );
        Assert.StartsWith( localApplicationData + Path.DirectorySeparatorChar, directories.ApplicationDataDirectory, StringComparison.Ordinal );
        Assert.StartsWith( directories.ApplicationDataDirectory + Path.DirectorySeparatorChar, directories.TempDirectory, StringComparison.Ordinal );
    }

    /// <summary>
    /// The locations that the service searches differ per operating system and per architecture.
    /// </summary>
    [PlatformFact( TestPlatforms.Unix )]
    public void TheDotNetExecutableIsTheOneThatRunsTheTests()
    {
        var platformInfo = this._serviceProvider.GetRequiredBackstageService<IPlatformInfo>();

        Assert.Equal( DotNetHost.ExecutablePath, Path.GetFullPath( platformInfo.DotNetExePath ) );
    }

    [PlatformFact( TestPlatforms.Linux )]
    public void TheMachineIdentifierIsTheOneOfTheOperatingSystem()
    {
        var expected = new[] { "/etc/machine-id", "/var/lib/dbus/machine-id" }
                           .Where( File.Exists )
                           .Select( path => File.ReadAllText( path ).Trim() )
                           .FirstOrDefault( id => id.Length > 0 )
                       ?? Environment.MachineName;

        Assert.Equal( expected, this._serviceProvider.GetRequiredBackstageService<IMachineIdProvider>().MachineId );
    }

    /// <summary>
    /// The identifier comes from the output of <c>ioreg</c>, whose format the unit tests fake.
    /// </summary>
    [PlatformFact( TestPlatforms.MacOS )]
    public void TheMachineIdentifierIsTheHardwareIdentifier()
    {
        var machineId = this._serviceProvider.GetRequiredBackstageService<IMachineIdProvider>().MachineId;

        Assert.True( Guid.TryParse( machineId, out _ ), $"'{machineId}' is not the hardware identifier." );
    }
}
