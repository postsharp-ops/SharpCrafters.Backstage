// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.ProcessClassification;
using SharpCrafters.Backstage.Testing;
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
}