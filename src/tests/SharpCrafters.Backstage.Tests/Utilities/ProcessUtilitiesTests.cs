// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Diagnostics;
using SharpCrafters.Backstage.Testing;
using SharpCrafters.Backstage.Utilities;
using Xunit;
using Xunit.Abstractions;

namespace SharpCrafters.Backstage.Tests.Utilities;

public sealed class ProcessUtilitiesTests : TestsBase
{
    public ProcessUtilitiesTests( ITestOutputHelper logger ) : base( logger ) { }

    [Fact]
    public void ParentProcessesCanBeRetrieved()
    {
        var logger = this.ServiceProvider.GetLoggerFactory().GetLogger( nameof(ProcessUtilitiesTests) );
        var parentProcesses = ProcessUtilities.GetParentProcesses( logger );

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
        => Assert.Equal( expected, ProcessUtilities.IsWindowsContainerAccount( userName, userDomainName ) );

    /// <summary>
    /// Asserts the relation between the two verdicts rather than either of them, because a test cannot put
    /// itself inside a container. It fails if the container check is ever removed from the unattended
    /// heuristics, which is what a product relies on when it reports nothing from a container.
    /// </summary>
    [Fact]
    public void ContainerImpliesUnattended()
    {
        var loggerFactory = this.ServiceProvider.GetLoggerFactory();

        if ( ProcessUtilities.IsRunningInContainer( loggerFactory ) )
        {
            Assert.True( ProcessUtilities.IsCurrentProcessUnattended( loggerFactory ) );
        }
    }
}
