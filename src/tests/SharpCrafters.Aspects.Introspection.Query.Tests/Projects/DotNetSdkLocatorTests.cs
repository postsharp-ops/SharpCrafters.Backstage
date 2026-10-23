// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Aspects.Introspection.Query.Projects;
using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace SharpCrafters.Aspects.Introspection.Query.Tests.Projects;

/// <summary>
/// The choice and the registration of the .NET SDK whose MSBuild evaluates the projects.
/// </summary>
public sealed class DotNetSdkLocatorTests : IDisposable
{
    private readonly string _root = Path.Combine( Path.GetTempPath(), "introspection-tests", Guid.NewGuid().ToString( "N" ) );

    public void Dispose()
    {
        if ( Directory.Exists( this._root ) )
        {
            Directory.Delete( this._root, true );
        }
    }

    private DotNetSdk Sdk( string version, string? runtimeIdentifier )
    {
        var directory = Path.Combine( this._root, version );
        Directory.CreateDirectory( directory );

        if ( runtimeIdentifier != null )
        {
            File.WriteAllLines( Path.Combine( directory, ".version" ), ["commit", version, runtimeIdentifier] );
        }

        return new DotNetSdk( version, directory );
    }

    [Fact]
    public void TheRegisteredSdkSetsTheEnvironmentOfMSBuild()
    {
        var registration = DotNetSdkLocator.EnsureRegistered();

        Assert.True( registration.Sdk != null, registration.Failure );

        // As MSBuildLocator does for the SDKs it discovers itself, so that the SDK resolvers and the imports find this SDK.
        var directory = registration.Sdk!.Directory;
        Assert.Equal( Path.Combine( directory, "MSBuild.dll" ), Environment.GetEnvironmentVariable( "MSBUILD_EXE_PATH" ) );
        Assert.Equal( directory, Environment.GetEnvironmentVariable( "MSBuildExtensionsPath" ) );
        Assert.Equal( Path.Combine( directory, "Sdks" ), Environment.GetEnvironmentVariable( "MSBuildSDKsPath" ) );

        // Once per process: the same registration.
        Assert.Same( registration, DotNetSdkLocator.EnsureRegistered() );
    }

    [Fact]
    public void AnSdkThatTheHostRegisteredIsDescribedFromItsDirectory()
    {
        var withVersionFile = this.Sdk( "10.0.100", "win-x64" );
        Assert.Equal( withVersionFile, DotNetSdkLocator.FromDirectory( withVersionFile.Directory ) );

        // Without a .version file, the name of the directory is the version.
        var withoutVersionFile = this.Sdk( "10.0.200", null );
        Assert.Equal( "10.0.200", DotNetSdkLocator.FromDirectory( withoutVersionFile.Directory + Path.DirectorySeparatorChar ).Version );
    }

    [Fact]
    public void TheChoiceSaysWhyEachSdkWasRejected()
    {
        DotNetSdk[] sdks = [this.Sdk( "11.0.100-rc.2.1", "win-x64" ), this.Sdk( "10.0.200", "win-arm64" ), this.Sdk( "10.0.100", null )];
        var rejections = new List<string>();

        Assert.Null( DotNetSdkLocator.Choose( sdks, new Version( 10, 0, 5 ), "win-x64", rejections ) );
        Assert.Equal(
            ["11.0.100-rc.2.1 (needs .NET 11 or later)", "10.0.200 (built for win-arm64)", $"10.0.100 (no .version file in '{sdks[2].Directory}')"],
            rejections );

        var message = DotNetSdkLocator.DescribeNoCompatibleSdk( sdks, rejections, new Version( 10, 0, 5 ), "win-x64" );
        Assert.StartsWith( "No .NET SDK can be loaded by this process, which runs .NET 10.0.5 on win-x64.", message, StringComparison.Ordinal );
        Assert.Contains( "10.0.200 (built for win-arm64)", message, StringComparison.Ordinal );
        Assert.Contains( "Install a .NET SDK of version 10 or lower for win-x64", message, StringComparison.Ordinal );

        Assert.Contains( "lists no SDK", DotNetSdkLocator.DescribeNoCompatibleSdk( [], [], new Version( 10, 0, 5 ), "win-x64" ), StringComparison.Ordinal );
    }

    [Fact]
    public void AnSdkListThatCannotBeObtainedSaysWhy()
    {
        var (sdks, failure) = DotNetSdkLocator.ListSdks( "no-such-dotnet-" + Guid.NewGuid().ToString( "N" ), TimeSpan.FromSeconds( 30 ) );

        Assert.Empty( sdks );
        Assert.Contains( "could not be started, so no .NET SDK can be found", failure, StringComparison.Ordinal );
    }

    [Fact]
    public void TheSdksAreListedByDotNet()
    {
        var (sdks, failure) = DotNetSdkLocator.ListSdks( "dotnet", TimeSpan.FromSeconds( 30 ) );

        Assert.Null( failure );
        Assert.NotEmpty( sdks );
    }
}
