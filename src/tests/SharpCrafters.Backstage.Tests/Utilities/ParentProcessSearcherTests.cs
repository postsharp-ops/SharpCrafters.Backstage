// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.ProcessClassification;
using SharpCrafters.Backstage.Testing;
using System;
using System.IO;
using Xunit;
using Xunit.Abstractions;

namespace SharpCrafters.Backstage.Tests.Utilities;

public sealed class ParentProcessSearcherTests : TestsBase
{
    public ParentProcessSearcherTests( ITestOutputHelper logger ) : base( logger ) { }

    [Fact]
    public void ParentProcessesCanBeRetrieved()
    {
        var parentProcessSearch = ParentProcessSearch.Create( this.ServiceProvider );
        var parentProcesses = parentProcessSearch.GetParentProcesses();

        Assert.NotEmpty( parentProcesses );

        // ReSharper disable once ParameterOnlyUsedForPreconditionCheck.Local
        Assert.All(
            parentProcesses,
            p =>
            {
                Assert.NotEqual( 0, p.ProcessId );
                Assert.NotNull( p.ProcessName );
                Assert.NotEmpty( p.ProcessName! );
            } );
    }

    [Theory]

    // The two accounts a Windows container runs its processes as.
    [InlineData( "ContainerUser", "User Manager", true )]
    [InlineData( "ContainerAdministrator", "User Manager", true )]
    [InlineData( "containeruser", "user manager", true )]

    // The authority is what tells a container apart from a machine on which somebody created an account of that
    // name.
    [InlineData( "ContainerUser", "BUILDAGENT", false )]
    [InlineData( "ContainerAdministrator", "CONTOSO", false )]

    // Ordinary accounts.
    [InlineData( "gael", "User Manager", false )]
    [InlineData( "SYSTEM", "NT AUTHORITY", false )]
    public void WindowsContainerIsDetectedFromTheAccount( string userName, string userDomainName, bool expected )
        => Assert.Equal( expected, ContainerDetection.IsWindowsContainerAccount( userName, () => userDomainName ) );

    [Fact]
    public void TheDomainNameIsNotReadForAnOrdinaryAccount()
    {
        // Reading Environment.UserDomainName can block on an account lookup, so it is read only for a container account.
        var isDomainNameRead = false;

        var result = ContainerDetection.IsWindowsContainerAccount(
            "gael",
            () =>
            {
                isDomainNameRead = true;

                return "User Manager";
            } );

        Assert.False( result );
        Assert.False( isDomainNameRead );
    }

    [Theory]
    [InlineData( "1234 (dotnet) S 1000 1234 1000 0 -1", 1000 )]

    // The name of the process is between parentheses and can contain spaces and parentheses.
    [InlineData( "1234 (Platform Test () S 1000 1234 1000 0 -1", 1000 )]
    [InlineData( "1234 (a) b (c)) R 42 1234 1000 0 -1", 42 )]
    public void TheParentProcessIdentifierIsReadFromTheLinuxStatus( string processStatus, int expected )
        => Assert.Equal( expected, ParentProcessSearchLinux.GetParentProcessId( processStatus ) );

    [Theory]

    // Linux keeps 15 characters of the name, so a name of that length is completed from the executable.
    [InlineData( "SharpCrafters.B", "/app/SharpCrafters.Backstage.PlatformTestHelper", "SharpCrafters.Backstage.PlatformTestHelper" )]

    // A process whose name was not taken from its executable, such as a script run by an interpreter, keeps its name.
    [InlineData( "long-script-nam", "/usr/bin/bash", "long-script-nam" )]
    [InlineData( "SharpCrafters.B", null, "SharpCrafters.B" )]
    public void ATruncatedLinuxNameIsCompletedFromTheExecutable( string commandName, string? executablePath, string expected )
        => Assert.Equal( expected, ParentProcessSearchLinux.GetUntruncatedName( commandName, executablePath ) );

    [Theory]
    [InlineData( "    1 /usr/local/share/dotnet/dotnet", 1, "dotnet" )]
    [InlineData( "  512 -zsh", 512, "-zsh" )]

    // The path of the executable can contain spaces.
    [InlineData( "  812 /Applications/Visual Studio Code.app/Contents/MacOS/Electron", 812, "Electron" )]
    [InlineData( "  812 /tmp/Platform Test (Helper)", 812, "Platform Test (Helper)" )]
    public void TheParentProcessIsReadFromTheOutputOfPs( string output, int expectedParentProcessId, string expectedImageName )
    {
        Assert.Equal( expectedImageName, ParentProcessSearchMac.GetImageName( output, out var parentProcessId ) );
        Assert.Equal( expectedParentProcessId, parentProcessId );
    }

    [Theory]
    [InlineData( ".dockerenv" )]
    [InlineData( "run/.containerenv" )]
    public void ALinuxContainerIsDetectedFromTheFileOfItsEngine( string markerFile )
    {
        // Under control groups v2, /proc/1/cgroup names no container engine.
        var root = Path.Combine( Path.GetTempPath(), "ContainerDetection", Guid.NewGuid().ToString( "N" ) );
        Directory.CreateDirectory( Path.Combine( root, "proc", "1" ) );
        File.WriteAllText( Path.Combine( root, "proc", "1", "cgroup" ), "0::/\n" );

        try
        {
            Assert.False( ContainerDetection.IsLinuxContainer( root, null ) );

            Directory.CreateDirectory( Path.GetDirectoryName( Path.Combine( root, markerFile ) )! );
            File.WriteAllText( Path.Combine( root, markerFile ), "" );

            Assert.True( ContainerDetection.IsLinuxContainer( root, null ) );
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }
}