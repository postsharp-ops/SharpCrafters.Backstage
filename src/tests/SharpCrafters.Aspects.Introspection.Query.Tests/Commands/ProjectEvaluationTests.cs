// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Aspects.Introspection.Query.Projects;
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace SharpCrafters.Aspects.Introspection.Query.Tests.Commands;

/// <summary>
/// The errors of the scope, the evaluation of the projects, and the search of the build outputs without an SDK.
/// </summary>
public sealed class ProjectEvaluationTests
{
    private readonly TestProvider _provider = new();

    private (int ExitCode, string Output, string Error) Run( string directory, params string[] arguments ) => CommandRunner.Run( directory, this._provider, arguments );

    private static string CreateDirectory()
    {
        var directory = Path.Combine( Path.GetTempPath(), "introspection-tests", Guid.NewGuid().ToString( "N" ) );
        Directory.CreateDirectory( directory );

        return directory;
    }

    [Theory]
    [InlineData( new[] { "--solution", "All.sln" }, "--solution is not supported yet" )]
    [InlineData( new[] { "--project", "Missing.csproj" }, "The project 'Missing.csproj' does not exist." )]
    public void AnInvalidScopeIsAnError( string[] options, string message )
    {
        using var project = new TestProject();

        var (exitCode, _, error) = this.Run( project.Directory, ["list", "aspect-classes", .. options] );

        Assert.Equal( IntrospectionExitCodes.Error, exitCode );
        Assert.Contains( message, error, StringComparison.Ordinal );
    }

    [Fact]
    public void AProjectAndASolutionCannotBothBeGiven()
    {
        using var project = new TestProject();

        var (exitCode, _, error) = this.Run( project.Directory, "list", "aspect-classes", "--project", "App.csproj", "--solution", "All.sln" );

        Assert.NotEqual( 0, exitCode );
        Assert.Contains( "--project and --solution cannot both be given.", error, StringComparison.Ordinal );
    }

    [Fact]
    public void ADirectoryWithASolutionOrNothingIsReported()
    {
        var directory = CreateDirectory();

        try
        {
            var (exitCode, _, error) = this.Run( directory, "list", "aspect-classes" );
            Assert.Equal( IntrospectionExitCodes.Error, exitCode );
            Assert.Contains( "holds no project or solution file: give one with --project or --solution.", error, StringComparison.Ordinal );

            File.WriteAllText( Path.Combine( directory, "All.sln" ), "" );
            (exitCode, _, error) = this.Run( directory, "list", "aspect-classes" );
            Assert.Equal( IntrospectionExitCodes.Error, exitCode );
            Assert.Contains( "Solutions are not supported yet", error, StringComparison.Ordinal );
        }
        finally
        {
            Directory.Delete( directory, true );
        }
    }

    [Fact]
    public void AMalformedFileIsAnError()
    {
        using var project = new TestProject( "<Symbols" );

        var (exitCode, _, error) = this.Run( project.Directory, "list", "aspect-classes" );

        Assert.Equal( IntrospectionExitCodes.Error, exitCode );
        Assert.Contains( "is malformed", error, StringComparison.Ordinal );
    }

    [Fact]
    public void AProjectThatCannotBeEvaluatedIsAnError()
    {
        using var project = new TestProject();
        File.WriteAllText( Path.Combine( project.Directory, "App.csproj" ), "<Project>" );

        var (exitCode, _, error) = this.Run( project.Directory, "list", "aspect-classes" );

        Assert.Equal( IntrospectionExitCodes.Error, exitCode );
        Assert.Contains( "cannot be evaluated", error, StringComparison.Ordinal );
    }

    [Fact]
    public void TheConfigurationOptionChoosesTheBuildOutputs()
    {
        using var project = new TestProject();
        var release = Path.Combine( project.Directory, "bin", "Release", "net10.0", "App.pssym" );
        Directory.CreateDirectory( Path.GetDirectoryName( release )! );
        File.WriteAllText( release, TestProject.DefaultSymbols.Replace( "App.CacheAttribute", "App.ReleaseCacheAttribute", StringComparison.Ordinal ) );

        var (exitCode, output, error) = this.Run( project.Directory, "list", "aspect-classes", "-c", "Release", "--format", "json" );

        Assert.True( exitCode == 0, error );
        var result = JsonDocument.Parse( output ).RootElement;
        Assert.Equal( "Release", CommandRunner.Files( project.Directory, this._provider, "-c", "Release" )[0].GetProperty( "configuration" ).GetString() );
        Assert.Contains( result.GetProperty( "items" ).EnumerateArray(), c => c.GetProperty( "name" ).GetString() == "App.ReleaseCacheAttribute" );
    }

    [Fact]
    public void ANewerProjectFileMakesTheFileStale()
    {
        using var project = new TestProject();
        File.SetLastWriteTimeUtc( Path.Combine( project.Directory, "App.csproj" ), DateTime.UtcNow.AddMinutes( 10 ) );

        Assert.True( CommandRunner.Files( project.Directory, this._provider )[0].GetProperty( "stale" ).GetBoolean() );
    }

    [Fact]
    public void AGeneratedSourceUnderObjDoesNotMakeTheFileStale()
    {
        // A source under obj, which the build regenerates, compiled explicitly and newer than the file.
        using var project = new TestProject( extraProperties: "<EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup><ItemGroup><Compile Include=\"obj/Generated.cs\" /></ItemGroup><PropertyGroup>" );
        var generated = Path.Combine( project.Directory, "obj", "Generated.cs" );
        Directory.CreateDirectory( Path.GetDirectoryName( generated )! );
        File.WriteAllText( generated, "" );
        File.SetLastWriteTimeUtc( generated, DateTime.UtcNow.AddMinutes( 10 ) );
        File.SetLastWriteTimeUtc( Path.Combine( project.Directory, "App.csproj" ), DateTime.UtcNow.AddMinutes( -10 ) );

        Assert.False( CommandRunner.Files( project.Directory, this._provider )[0].GetProperty( "stale" ).GetBoolean() );
    }

    [Fact]
    public void AFrameworkThatTheProjectDoesNotTargetIsNamed()
    {
        using var project = new TestProject();

        var (exitCode, _, error) = this.Run( project.Directory, "list", "aspect-classes", "-f", "net9.0" );

        Assert.Equal( IntrospectionExitCodes.Error, exitCode );
        Assert.Contains( "The project 'App' does not target net9.0: its target frameworks are net10.0.", error, StringComparison.Ordinal );
    }

    [Fact]
    public void AProjectThatIsNotSdkStyleIsEvaluatedOnce()
    {
        using var project = new TestProject( symbols: null );

        File.WriteAllText(
            Path.Combine( project.Directory, "App.csproj" ),
            """
            <Project ToolsVersion="15.0">
              <PropertyGroup>
                <Configuration Condition="'$(Configuration)' == ''">Debug</Configuration>
                <TargetFrameworkVersion>v4.7.2</TargetFrameworkVersion>
                <AssemblyName>App</AssemblyName>
                <TestSymbolFile>bin\$(Configuration)\$(AssemblyName).pssym</TestSymbolFile>
              </PropertyGroup>
            </Project>
            """ );

        File.SetLastWriteTimeUtc( Path.Combine( project.Directory, "App.csproj" ), DateTime.UtcNow.AddMinutes( -10 ) );
        Directory.CreateDirectory( Path.Combine( project.Directory, "bin", "Debug" ) );
        File.WriteAllText( Path.Combine( project.Directory, "bin", "Debug", "App.pssym" ), TestProject.DefaultSymbols );

        foreach ( var options in new[] { Array.Empty<string>(), ["-f", "net472"] } )
        {
            var (exitCode, output, error) = this.Run( project.Directory, ["list", "aspect-classes", "--format", "json", .. options] );

            Assert.True( exitCode == 0, error );
            var result = JsonDocument.Parse( output ).RootElement;
            Assert.Equal( "net472", CommandRunner.Files( project.Directory, this._provider, options )[0].GetProperty( "targetFramework" ).GetString() );
            Assert.Equal( 3, result.GetProperty( "count" ).GetInt32() );
        }
    }

    [Theory]
    [InlineData( "v4.7.2", "net472" )]
    [InlineData( "v4.8", "net48" )]
    [InlineData( "4.6.1", "net461" )]
    [InlineData( "", null )]
    [InlineData( "vNext", null )]
    public void TheVersionOfALegacyProjectIsAShortFrameworkName( string version, string? framework )
        => Assert.Equal( framework, MSBuildProjectEvaluator.ToShortFrameworkName( version ) );

    [Fact]
    public void TheSearchDecodesTheRuntimeIdentifierAndTheArtifactsLayout()
    {
        var root = CreateDirectory();

        try
        {
            // A project under src, with the artifacts output layout at the root of the repository.
            var projectPath = Path.Combine( root, "src", "App", "App.csproj" );
            Directory.CreateDirectory( Path.GetDirectoryName( projectPath )! );
            File.WriteAllText( projectPath, "<Project />" );
            File.SetLastWriteTimeUtc( projectPath, DateTime.UtcNow.AddMinutes( -10 ) );

            void Write( params string[] path )
            {
                var file = Path.Combine( [root, .. path] );
                Directory.CreateDirectory( Path.GetDirectoryName( file )! );
                File.WriteAllText( file, TestProject.DefaultSymbols );
            }

            // The file of the build, and those of a runtime identifier and of a publish, which are left out.
            Write( "src", "App", "bin", "Debug", "net10.0", "App.pssym" );
            Write( "src", "App", "bin", "Debug", "net10.0", "win-x64", "App.pssym" );
            Write( "src", "App", "bin", "Debug", "net10.0", "publish", "App.pssym" );
            Write( "artifacts", "bin", "App", "release_net48", "App.pssym" );
            Write( "artifacts", "bin", "App", "release_net48_win-x64", "App.pssym" );
            Write( "artifacts", "bin", "App", "debug_net10.0_linux-x64", "App.pssym" );

            // A framework of which only the output of a runtime identifier exists, and another project.
            Write( "artifacts", "bin", "App", "debug_net9.0_linux-x64", "App.pssym" );
            Write( "artifacts", "bin", "Other", "debug_net10.0", "Other.pssym" );

            var found = ProjectLocator.Search( projectPath, this._provider, null, null );

            Assert.Equal(
                ["Debug net10.0", "debug net9.0", "release net48"],
                found.Targets.Select( t => $"{t.Configuration} {t.TargetFramework}" ).Order( StringComparer.Ordinal ) );

            Assert.Equal( Path.Combine( root, "src", "App", "bin", "Debug", "net10.0", "App.pssym" ), found.Targets.Single( t => t.TargetFramework == "net10.0" ).FilePath );
            Assert.Equal( Path.Combine( root, "artifacts", "bin", "App", "release_net48", "App.pssym" ), found.Targets.Single( t => t.TargetFramework == "net48" ).FilePath );

            Assert.Equal(
                ["release net48"],
                ProjectLocator.Search( projectPath, this._provider, "Release", "net48" ).Targets.Select( t => $"{t.Configuration} {t.TargetFramework}" ) );

            Assert.Equal( 2, ProjectLocator.Search( projectPath, this._provider, "Debug", null ).Targets.Count );
        }
        finally
        {
            Directory.Delete( root, true );
        }
    }

    [Fact]
    public void ASearchedFileOlderThanTheProjectFileIsStale()
    {
        using var project = new TestProject();
        var projectPath = Path.Combine( project.Directory, "App.csproj" );

        Assert.False( Assert.Single( ProjectLocator.Search( projectPath, this._provider, null, null ).Targets ).IsStale );

        File.SetLastWriteTimeUtc( projectPath, DateTime.UtcNow.AddMinutes( 10 ) );
        Assert.True( Assert.Single( ProjectLocator.Search( projectPath, this._provider, null, null ).Targets ).IsStale );
    }

    [Theory]
    [InlineData( "11.0.100-rc.2.1", "11.0.100-rc.1.1" )]
    [InlineData( "11.0.100", "11.0.100-rc.2.1" )]
    [InlineData( "11.0.100-rc.10.1", "11.0.100-rc.9.1" )]
    [InlineData( "11.0.100-rc.1", "11.0.100-preview.7" )]
    [InlineData( "11.0.100-rc.1.1", "11.0.100-rc.1" )]
    [InlineData( "10.0.200", "10.0.100" )]
    public void ASdkVersionIsAboveAnother( string higher, string lower )
    {
        Assert.True( SdkVersionComparer.Instance.Compare( higher, lower ) > 0 );
        Assert.True( SdkVersionComparer.Instance.Compare( lower, higher ) < 0 );
    }

    [Fact]
    public void TheHighestPrereleaseOfAnSdkIsChosen()
    {
        var root = CreateDirectory();

        try
        {
            DotNetSdk Sdk( string version )
            {
                var directory = Path.Combine( root, version );
                Directory.CreateDirectory( directory );
                File.WriteAllLines( Path.Combine( directory, ".version" ), ["commit", version, "win-x64"] );

                return new DotNetSdk( version, directory );
            }

            Assert.Equal(
                "11.0.100-rc.2.1",
                DotNetSdkLocator.Choose( [Sdk( "11.0.100-rc.1.1" ), Sdk( "11.0.100-rc.2.1" ), Sdk( "11.0.100-preview.7.1" )], new Version( 11, 0, 0 ), "win-x64" )?.Version );
        }
        finally
        {
            Directory.Delete( root, true );
        }
    }

    [Fact]
    public void TheProviderAddsItsCommandsAndItsFiles()
    {
        using var project = new TestProject( extraProperties: "<TestSymbolLocationFile>bin/$(Configuration)/$(TargetFramework)/$(AssemblyName).pssdb</TestSymbolLocationFile>" );

        var (exitCode, output, error) = this.Run( project.Directory, "additional-files" );

        Assert.True( exitCode == 0, error );
        Assert.Equal(
            Path.Combine( project.Directory, "bin", "Debug", "net10.0", "App.pssdb" ),
            JsonDocument.Parse( output ).RootElement.GetProperty( "TestSymbolLocationFile" ).GetString() );
    }
}
