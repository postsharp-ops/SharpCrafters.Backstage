// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools;
using PostSharp.Engineering.BuildTools.Build.Model;
using PostSharp.Engineering.BuildTools.Build.Solutions;
using PostSharp.Engineering.BuildTools.Docker;
using FoundationsDependencies = PostSharp.Engineering.BuildTools.Dependencies.Definitions.FoundationsDependencies.V2027_0;

// The .NET 11 SDK, which global.json names as the main SDK of the product and which the build container installs.
// The version is a literal instead of a member of the product family, because the .NET 11 SDK is a prerelease and
// PostSharp.Engineering names only released feature bands. Keep it equal to the constant of the same name in the
// Metalama repository, which consumes the packages of this repository, and move both to
// FoundationsDependencies.V2027_0.Family.PreferredVersions.DotNetSdk once the .NET 11 SDK is released.
const string dotNet11SdkVersion = "11.0.100-rc.1.26425.128";

// The .NET 10 SDK, which stays installed beside the .NET 11 one, because the build tool of this repository targets
// net10.0 and the .NET 11 SDK carries no .NET 10 runtime. The version comes from the product family, so that every
// product of the family requests the same feature band and the container layers are shared.
var dotNet10SdkVersion = FoundationsDependencies.Family.PreferredVersions.DotNetSdk.V_10_0;

var product = new Product( FoundationsDependencies.Foundations )
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
    DotNetSdkVersion = new DotNetSdkVersion( dotNet11SdkVersion ) { AllowPrerelease = true },

    Solutions = [new DotNetSolution( "SharpCrafters.Foundations.sln" ) { CanFormatCode = true }]

    // The solution contains no project yet, so the build produces no package and there is nothing to publish.
    // Add the package names to PublicArtifacts when the first project is added.
};

return new EngineeringApp( product ).Run( args );
