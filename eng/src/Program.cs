// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using PostSharp.Engineering.BuildTools;
using PostSharp.Engineering.BuildTools.Build;
using PostSharp.Engineering.BuildTools.Build.Model;
using PostSharp.Engineering.BuildTools.Build.Solutions;
using PostSharp.Engineering.BuildTools.ContinuousIntegration.Model;
using PostSharp.Engineering.BuildTools.Docker;
using BackstageDependencies = PostSharp.Engineering.BuildTools.Dependencies.Definitions.BackstageDependencies.V2027_0;

// The .NET 11 SDK, which the build container installs beside the .NET 10 one so that the product can be built
// against it on demand. The version is a literal instead of a member of the product family, because the .NET 11 SDK
// is a prerelease and PostSharp.Engineering names only released feature bands. Keep it equal to the constant of the
// same name in the Metalama repository, which consumes the packages of this repository, and move both to
// BackstageDependencies.V2027_0.Family.PreferredVersions.DotNetSdk once the .NET 11 SDK is released.
const string dotNet11SdkVersion = "11.0.100-rc.1.26425.128";

// The .NET 10 SDK, which global.json names as the main SDK of the product. The version comes from the product
// family, so that every product of the family requests the same feature band and the container layers are shared.
//
// It is this SDK and not the .NET 11 one, although the latter is the newer, because the Razor source generator of
// 11.0.100-rc.1.26425.128 miscompiles the pages of SharpCrafters.Backstage.Worker: it emits the assignment of a tag
// helper attribute with a source span that is off by two columns, so that
// `<div asp-validation-summary="ModelOnly">` generates `ValidationSummary.` followed by `="ModelOn`, which does not
// compile. Three pages are affected and the build of the whole solution fails from a clean output directory. No
// project of this repository targets net11.0, so nothing is lost by building with the released SDK. Move back to
// `dotNet11SdkVersion` once a .NET 11 SDK that compiles those pages is available.
var dotNet10SdkVersion = BackstageDependencies.Family.PreferredVersions.DotNetSdk.V_10_0;

var product = new Product( BackstageDependencies.Backstage )
{
    OverriddenBuildAgentRequirements = new ContainerRequirements( ContainerHostKind.Windows )
    {
        Components =
        [
            new DotNetComponent( dotNet11SdkVersion, DotNetComponentKind.Sdk ),
            new DotNetComponent( dotNet10SdkVersion, DotNetComponentKind.Sdk )
        ]
    },
    GenerateNuGetConfig = true,

    // Writes nuget.wsl.config beside nuget.config. Its sources are the same, but with the paths of the engine inside
    // WSL, which is where DockerBuild.ps1 runs a Linux test container on a Windows development machine.
    AddWslSupport = true,
    DotNetSdkVersion = new DotNetSdkVersion( dotNet10SdkVersion ),

    Solutions = [new DotNetSolution( "SharpCrafters.Backstage.sln" ) { SupportsTestCoverage = true, CanFormatCode = true }],

    // TODO: Should be reviewed before publishing first release.
    
    // Only the packages that a public package of a product depends on are public. SharpCrafters.Backstage.Testing
    // is consumed by test projects only, and the worker and Windows libraries are project references of the
    // product executables, so they are not packed at all.
    PublicArtifacts = Pattern.Create(
        "SharpCrafters.Backstage.$(PackageVersion).nupkg",
        "SharpCrafters.Backstage.Commands.$(PackageVersion).nupkg", // Required by Metalama.Tool.
        "SharpCrafters.Common.$(PackageVersion).nupkg",             // Required by Metalama.Framework.Engine and Metalama.Patterns.Caching.Backend.
        "SharpCrafters.Common.Abstractions.$(PackageVersion).nupkg",             // Required by SharpCrafters.Common and SharpCrafters.Backstage.Threading.
        "SharpCrafters.Backstage.Abstractions.$(PackageVersion).nupkg",          // Required by the three packages below.
        "SharpCrafters.Backstage.Threading.$(PackageVersion).nupkg",             // Required by SharpCrafters.Backstage.
        "SharpCrafters.Backstage.FileLocks.$(PackageVersion).nupkg",             // Required by SharpCrafters.Backstage.
        "SharpCrafters.Backstage.ProcessClassification.$(PackageVersion).nupkg", // Required by SharpCrafters.Backstage.
        "Metalama.Backstage.$(PackageVersion).nupkg",               // Required by Metalama.Framework.
        "Metalama.Backstage.Tools.$(PackageVersion).nupkg",         // Required by Metalama.Framework.Engine and Metalama.Vsx.
        "PostSharp.Backstage.$(PackageVersion).nupkg",              // Required by PostSharp.
        "PostSharp.Backstage.Tools.$(PackageVersion).nupkg" ),      // Required by PostSharp and PostSharp.Vsx.

    // The platform tests in src/tests/Platform run the platform-specific code of the packages on the platform itself.
    // They consume the packages of the Debug build. Linux and Windows run in a container, one per suite. macOS runs on
    // the agent, because no container engine provides a macOS container.
    AdditionalCiBuildConfigurations =
    [
        ..DockerTestsAdditionalCiBuildConfiguration.WithCompositeConfiguration(
            CreateDockerTestConfiguration( DockerTestPlatform.WindowsX64, "Windows x64" ),
            CreateDockerTestConfiguration( DockerTestPlatform.LinuxX64, "Linux x64" ),
            CreateDockerTestConfiguration( DockerTestPlatform.LinuxArm64, "Linux ARM64" ) ),
        new PowershellAdditionalCiBuildConfiguration(
            "PlatformTestsMacOSArm64",
            "Platform Tests (macOS ARM64)",
            "src/tests/Platform/RunMacOSTests.ps1",
            "" )
        {
            BuildSnapshotDependency = BuildConfiguration.Debug,
            ProjectFolder = DockerTestsAdditionalCiBuildConfiguration.DefaultProjectFolder,
            BuildAgentRequirements = new BuildAgentRequirements(
                new BuildAgentRequirement( "teamcity.agent.jvm.os.name", "Mac OS X" ),
                new BuildAgentRequirement( "teamcity.agent.jvm.os.arch", "aarch64" ) )
        }
    ]
};

return new EngineeringApp( product ).Run( args );

static DockerTestsAdditionalCiBuildConfiguration CreateDockerTestConfiguration( DockerTestPlatform platform, string title )
    => new( $"DockerTests{platform}", $"Docker Tests ({title})", platform, "src/tests/Platform/Docker" )
    {
        BuildSnapshotDependency = BuildConfiguration.Debug
    };
