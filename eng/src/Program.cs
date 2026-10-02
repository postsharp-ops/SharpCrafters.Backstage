// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using PostSharp.Engineering.BuildTools;
using PostSharp.Engineering.BuildTools.Build;
using PostSharp.Engineering.BuildTools.Build.Model;
using PostSharp.Engineering.BuildTools.Build.Solutions;
using PostSharp.Engineering.BuildTools.ContinuousIntegration.Model;
using PostSharp.Engineering.BuildTools.ContinuousIntegration.TeamCity.Arguments;
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

// The TeamCity sub-project of the build configurations of the platform tests.
const string platformTestsFolder = "Platform Tests";

// The environment variable that declares the kind of host to the platform tests. Keep it equal to
// PlatformConditions.HostVariableName in SharpCrafters.Backstage.Testing.
const string platformTestHostVariable = "BACKSTAGE_PLATFORM_TEST_HOST";

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

    // Writes each TeamCity build configuration to its own file under .teamcity/buildTypes instead of into settings.kts.
    GenerateTeamCityBuildTypesInSeparateFiles = true,
    DotNetSdkVersion = new DotNetSdkVersion( dotNet10SdkVersion ),

    // The test projects are xunit.v3 applications of Microsoft.Testing.Platform. Build.ps1 test runs them with
    // `dotnet test` in the mode of that platform, and Build.ps1 build packs them into test archives, which the
    // TestAgents below run. See doc/testing-platform.md in PostSharp.Engineering.
    TestRunner = TestRunner.MicrosoftTestingPlatform,

    // PackRequiresExplicitBuild builds the whole solution before `dotnet pack`, which skips the projects that are not
    // packable, among them the test projects. Their build is what writes the test archives.
    Solutions =
    [
        new DotNetSolution( "SharpCrafters.Backstage.sln" )
        {
            SupportsTestCoverage = true, CanFormatCode = true, ContainsTestApplications = true, PackRequiresExplicitBuild = true
        }
    ],

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

    // The Linux images of the platform tests. The product builds on Windows only, so these images run the test archives
    // and build nothing. They carry PowerShell, which RunTests.ps1 needs, and the .NET SDK rather than the runtime only,
    // because the product looks for a dotnet executable that has an SDK, and the platform tests check which one it finds.
    AdditionalDockerfiles =
    [
        CreateLinuxTestDockerfile( ContainerArchitecture.X64, dotNet10SdkVersion ),
        CreateLinuxTestDockerfile( ContainerArchitecture.Arm64, dotNet10SdkVersion )
    ],

    // Forwards the kind of host that the build configurations of the TestAgents declare into the test container. See
    // PlatformConditions.HostVariableName.
    AdditionalDockerEnvironmentVariables = [platformTestHostVariable],

    // The platform tests, SharpCrafters.Backstage.PlatformTests, run the platform-specific code of the product on the
    // platform itself. The agents run the test archives of the Debug build. Linux and Windows run them in a container.
    // macOS runs them on the agent, because no container engine provides a macOS container. The unit tests run in the
    // build, and their archives are skipped (see src/tests/Directory.Build.targets).
    Configurations = Product.DefaultConfigurations.WithValue( BuildConfiguration.Debug, c => c with { RunsTestArchives = true } ),
    TestAgents =
    [
        new TestAgent( "win-x64", "PlatformTestsWinX64", "Platform Tests Windows x64", new ContainerHostRequirements( ContainerHostKind.Windows ) )
        {
            Dockerfile = "eng/docker/build.Dockerfile", ProjectFolder = platformTestsFolder, Parameters = [CreateHostParameter( "container" )]
        },
        new TestAgent(
            "linux-x64",
            "PlatformTestsLinuxX64",
            "Platform Tests Linux x64",
            CreateLinuxContainerHostRequirements( ContainerArchitecture.X64 ) )
        {
            Dockerfile = "eng/docker/linux-x64-build.Dockerfile", ProjectFolder = platformTestsFolder, Parameters = [CreateHostParameter( "container" )]
        },
        new TestAgent(
            "linux-arm64",
            "PlatformTestsLinuxArm64",
            "Platform Tests Linux ARM64",
            CreateLinuxContainerHostRequirements( ContainerArchitecture.Arm64 ) )
        {
            Dockerfile = "eng/docker/linux-arm64-build.Dockerfile", ProjectFolder = platformTestsFolder, Parameters = [CreateHostParameter( "container" )]
        },

        // Plain BuildAgentRequirements, not ContainerHostRequirements, so that the tests run on the macOS host.
        new TestAgent(
            "osx-arm64",
            "PlatformTestsMacOSArm64",
            "Platform Tests macOS ARM64",
            new BuildAgentRequirements(
                new BuildAgentRequirement( "teamcity.agent.jvm.os.name", "Mac OS X" ),
                new BuildAgentRequirement( "teamcity.agent.jvm.os.arch", "aarch64" ) ) )
        {
            ProjectFolder = platformTestsFolder, Parameters = [CreateHostParameter( "host" )]
        }
    ]
};

return new EngineeringApp( product ).Run( args );

// Declares the kind of host to the platform tests, which compare it with what the product detects.
static BuildConfigurationParameter CreateHostParameter( string host ) => new( $"env.{platformTestHostVariable}", host );

static AdditionalDockerfile CreateLinuxTestDockerfile( ContainerArchitecture architecture, string dotNetSdkVersion )
    => new( architecture == ContainerArchitecture.Arm64 ? "linux-arm64" : "linux-x64", [] )
    {
        Requirements = new ContainerRequirements( ContainerHostKind.Linux )
        {
            OperatingSystem = ContainerOperatingSystem.Linux,
            Components =
            [
                new GitComponent(),
                new PowershellComponent( architecture ),
                new DotNetComponent( dotNetSdkVersion, DotNetComponentKind.Sdk )
            ]
        }
    };

// The operating system and the architecture that the agents report, as the Linux build configurations of PostSharp
// require them. An ARM64 Linux container runs on an ARM64 Linux agent or on the engine of the macOS agent.
static ContainerHostRequirements CreateLinuxContainerHostRequirements( ContainerArchitecture architecture )
    => new ContainerHostRequirements( ContainerHostKind.Linux ) with
    {
        Items = architecture == ContainerArchitecture.Arm64
            ?
            [
                new BuildAgentRequirement( "teamcity.agent.jvm.os.name", "Linux|Mac OS X", RequirementComparisonType.Matches ),
                new BuildAgentRequirement( "teamcity.agent.jvm.os.arch", "aarch64" )
            ]
            :
            [
                new BuildAgentRequirement( "teamcity.agent.jvm.os.name", "Linux" ),
                new BuildAgentRequirement( "teamcity.agent.jvm.os.arch", "amd64" )
            ]
    };
