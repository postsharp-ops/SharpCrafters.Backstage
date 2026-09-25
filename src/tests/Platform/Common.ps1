# Shared by every Docker test of the platform tests. Dot-sourced by each Docker/<Suite>/RunTest.ps1.
#
# A Docker test runs on the agent, not in the build container, so nothing here may need a tool chain. It invokes
# DockerBuild.ps1, which starts a container of the image of the platform, and the container runs RunSuite.ps1.

$ErrorActionPreference = 'Stop'

function Invoke-PlatformTestSuite
{
    param(
        # The platform identifier that the launcher selected, for example 'linux-x64'.
        [Parameter( Mandatory = $true )] [string] $Platform,

        # The suite, which is a namespace of PlatformTests, for example 'NamedLocks'.
        [Parameter( Mandatory = $true )] [string] $Suite
    )

    $containerOs = if ($Platform -like 'linux-*') { 'linux' } else { 'windows' }
    $dockerBuild = Join-Path ( Resolve-Path ( Join-Path $PSScriptRoot '../../..' ) ).Path 'DockerBuild.ps1'

    # A hashtable, not an array. Splatting an array does not bind the named parameters of DockerBuild.ps1: they fall into
    # its -BuildArgs parameter, and the script runs an ordinary product build instead of the test container.
    $arguments = @{
        Test = $true
        OS = $containerOs
        Dockerfile = Join-Path $PSScriptRoot "Images/$containerOs/Dockerfile"

        # The tests compare what the product detects with the kind of host that this variable declares.
        Env = @( 'BACKSTAGE_PLATFORM_TEST_HOST=container' )

        # The command runs in the mounted repository, through the shell of the image, which is sh or cmd. The SDK images
        # carry PowerShell, so the rest is the same script on every platform.
        Command = "pwsh -NoProfile -File src/tests/Platform/RunSuite.ps1 -Suite $Suite -Run $Suite-$Platform"
    }

    # Nothing is returned. The output of the container arrives on the standard output of this call, and the caller reads
    # $LASTEXITCODE.
    & $dockerBuild @arguments
}
