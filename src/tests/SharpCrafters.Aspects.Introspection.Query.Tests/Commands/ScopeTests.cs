// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Aspects.Introspection.Query.Projects;
using System;
using System.IO;
using System.Linq;
using Xunit;

namespace SharpCrafters.Aspects.Introspection.Query.Tests.Commands;

/// <summary>
/// The scope of a command, the location of its files, and the choice of the .NET SDK.
/// </summary>
public sealed class ScopeTests
{
    private readonly TestProvider _provider = new();

    [Fact]
    public void AMissingFileSaysToBuild()
    {
        using var project = new TestProject( symbols: null );

        var (exitCode, _, error) = CommandRunner.Run( project.Directory, this._provider, ["list", "aspect-classes"] );

        Assert.Equal( IntrospectionExitCodes.NoFile, exitCode );
        Assert.Contains( "(Debug, net10.0) does not exist. Build the project", error, StringComparison.Ordinal );
    }

    [Fact]
    public void AProjectOfAnotherProductIsReported()
    {
        using var project = new TestProject( setProperty: false );

        var (exitCode, _, error) = CommandRunner.Run( project.Directory, this._provider, ["list", "aspect-classes"] );

        Assert.Equal( IntrospectionExitCodes.NoFile, exitCode );
        Assert.Contains( "does not set TestSymbolFile: it does not use TestProduct", error, StringComparison.Ordinal );
    }

    [Fact]
    public void SeveralProjectsInTheDirectoryAreListed()
    {
        using var project = new TestProject();
        File.WriteAllText( Path.Combine( project.Directory, "Other.vbproj" ), "<Project />" );

        var (exitCode, _, error) = CommandRunner.Run( project.Directory, this._provider, ["list", "aspect-classes"] );

        Assert.Equal( IntrospectionExitCodes.Error, exitCode );
        Assert.Contains( "holds several project or solution files", error, StringComparison.Ordinal );
        Assert.Contains( "App.csproj, Other.vbproj", error, StringComparison.Ordinal );

        // --project chooses one, as a file or as its directory.
        Assert.Equal( 0, CommandRunner.Run( project.Directory, this._provider, ["list", "aspect-classes", "--project", "App.csproj"] ).ExitCode );
    }

    [Fact]
    public void AProjectIsGivenByItsDirectory()
    {
        using var project = new TestProject();
        var parent = Path.GetDirectoryName( project.Directory )!;

        var (exitCode, _, error) = CommandRunner.Run( parent, this._provider, ["list", "aspect-classes", "--project", Path.GetFileName( project.Directory )] );

        Assert.True( exitCode == 0, error );
    }

    [Fact]
    public void TheFrameworkOptionRestrictsTheTargetFrameworks()
    {
        using var project = new TestProject( targetFrameworks: "<TargetFrameworks>net10.0;net48</TargetFrameworks>" );
        project.WriteSymbols( "net48", TestProject.DefaultSymbols );

        var (exitCode, output, error) = CommandRunner.Run( project.Directory, this._provider, ["list", "aspect-classes", "-f", "net48", "--format", "json"] );

        Assert.True( exitCode == 0, error );
        Assert.False( output.Contains( "\"files\"", System.StringComparison.Ordinal ) );
        Assert.Equal( "net48", Assert.Single( CommandRunner.Files( project.Directory, this._provider, "-f", "net48" ) ).GetProperty( "targetFramework" ).GetString() );
    }

    [Fact]
    public void WithoutAnSdkTheBuildOutputsAreSearched()
    {
        using var project = new TestProject();
        project.WriteSymbols( "net48", TestProject.DefaultSymbols );

        var found = ProjectLocator.Search( Path.Combine( project.Directory, "App.csproj" ), this._provider, null, null );

        Assert.False( found.IsEvaluated );
        Assert.Equal( ["net10.0", "net48"], found.Targets.Select( t => t.TargetFramework ) );
        Assert.All( found.Targets, t => Assert.Equal( "Debug", t.Configuration ) );
        Assert.All( found.Targets, t => Assert.True( t.Exists ) );

        Assert.Equal( ["net48"], ProjectLocator.Search( Path.Combine( project.Directory, "App.csproj" ), this._provider, null, "net48" ).Targets.Select( t => t.TargetFramework ) );
    }

    [Fact]
    public void ACommandOfTheProviderReadsTheTargetsOfTheScope()
    {
        using var project = new TestProject( targetFrameworks: "<TargetFrameworks>net10.0;net48</TargetFrameworks>" );

        var (exitCode, output, error) = CommandRunner.Run( project.Directory, this._provider, ["files"] );

        // The file of net48 is missing: it is left out with a warning, as list and show leave it out.
        Assert.True( exitCode == 0, error );
        Assert.Equal( "[\"App net10.0\"]", output.Trim() );
        Assert.Contains( "warning: the file of 'App' (Debug, net48) does not exist", error, StringComparison.Ordinal );

        // Without any file, the command fails as list and show do.
        using var empty = new TestProject( symbols: null );
        Assert.Equal( IntrospectionExitCodes.NoFile, CommandRunner.Run( empty.Directory, this._provider, ["files"] ).ExitCode );
    }

    [Fact]
    public void TheSdkIsTheHighestThatTheRuntimeCanLoad()
    {
        var root = Path.Combine( Path.GetTempPath(), "introspection-tests", Guid.NewGuid().ToString( "N" ) );

        try
        {
            DotNetSdk Sdk( string version, string runtimeIdentifier )
            {
                var directory = Path.Combine( root, version );
                Directory.CreateDirectory( directory );
                File.WriteAllLines( Path.Combine( directory, ".version" ), ["commit", version, runtimeIdentifier] );

                return new DotNetSdk( version, directory );
            }

            var sdks = new[] { Sdk( "8.0.400", "win-x64" ), Sdk( "10.0.100", "win-x64" ), Sdk( "10.0.200", "win-arm64" ), Sdk( "11.0.100-rc.2.1", "win-x64" ) };

            Assert.Equal( "10.0.100", DotNetSdkLocator.Choose( sdks, new Version( 10, 0, 5 ), "win-x64" )?.Version );
            Assert.Equal( "11.0.100-rc.2.1", DotNetSdkLocator.Choose( sdks, new Version( 11, 0, 0 ), "win-x64" )?.Version );
            Assert.Equal( "10.0.200", DotNetSdkLocator.Choose( sdks, new Version( 10, 0, 5 ), "win-arm64" )?.Version );
            Assert.Null( DotNetSdkLocator.Choose( sdks, new Version( 7, 0, 0 ), "win-x64" ) );
        }
        finally
        {
            Directory.Delete( root, true );
        }
    }

    [Fact]
    public void TheListOfSdksIsParsed()
    {
        var sdks = DotNetSdkLocator.ParseSdkList( "8.0.400 [C:\\Program Files\\dotnet\\sdk]\r\n10.0.100-rc.1.25451.107 [/usr/share/dotnet/sdk]\nnot an sdk\n" );

        Assert.Equal( ["8.0.400", "10.0.100-rc.1.25451.107"], sdks.Select( s => s.Version ) );
        Assert.Equal( Path.Combine( "/usr/share/dotnet/sdk", "10.0.100-rc.1.25451.107" ), sdks[1].Directory );
    }
}
