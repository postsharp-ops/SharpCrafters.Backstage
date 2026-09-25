// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Application;
using SharpCrafters.Backstage.Extensibility;
using SharpCrafters.Backstage.Infrastructure;
using SharpCrafters.Backstage.ProcessClassification;
using SharpCrafters.Backstage.Testing;
using System.IO;
using System.Runtime.InteropServices;
using Xunit;
using Xunit.Abstractions;

namespace SharpCrafters.Backstage.Tests.Infrastructure;

/// <summary>
/// Tests the Rider rule of <see cref="PlatformInfo"/> when the application declares no process kind, which is the case
/// of every application derived from <see cref="ApplicationInfoBase"/>. The kind then comes from the detection of
/// <see cref="IApplicationInfoProvider"/>, which these tests replace with a fixed answer.
/// </summary>
public sealed class PlatformInfoDetectedProcessKindTests : TestsBase
{
    private ProcessKind _detectedProcessKind = ProcessKind.Other;

    public PlatformInfoDetectedProcessKindTests( ITestOutputHelper logger ) : base( logger )
    {
        this.ApplicationInfo = new TestApplicationInfo { ProcessKind = null };
    }

    protected override void ConfigureServices( ServiceProviderBuilder services )
        => services.AddSingleton<IApplicationInfoProvider>(
            new ApplicationInfoProvider( this.ApplicationInfo, () => this._detectedProcessKind ) );

    [Theory]
    [InlineData( ProcessKind.Rider, false )]
    [InlineData( ProcessKind.Other, true )]
    public void EnvironmentHintsAreSkippedWhenRiderIsDetected( ProcessKind detectedProcessKind, bool expectHint )
    {
        this._detectedProcessKind = detectedProcessKind;
        this.RuntimeInformation.TestProcessArchitecture = Architecture.X64;
        this.RuntimeInformation.Platform = OSPlatform.Windows;

        const string riderDotnet = "C:\\Rider\\dotnet\\dotnet.exe";
        const string programFiles = "C:\\Program Files";
        var systemDotnet = Path.Combine( programFiles, "dotnet", "dotnet.exe" );

        this.EnvironmentVariableProvider.Environment["DOTNET_HOST_PATH"] = riderDotnet;
        this.EnvironmentVariableProvider.Environment["ProgramFiles"] = programFiles;
        this.CreateDotNetWithSdk( riderDotnet );
        this.CreateDotNetWithSdk( systemDotnet );

        var result = this.ServiceProvider.GetRequiredBackstageService<IPlatformInfo>().DotNetExePath;

        Assert.Equal( expectHint ? riderDotnet : systemDotnet, result );
    }

    private void CreateDotNetWithSdk( string dotnetExePath )
    {
        this.FileSystem.CreateDirectory( Path.GetDirectoryName( dotnetExePath )! );
        this.FileSystem.WriteAllText( dotnetExePath, string.Empty );
        this.FileSystem.CreateDirectory( Path.Combine( Path.GetDirectoryName( dotnetExePath )!, "sdk" ) );
    }
}
