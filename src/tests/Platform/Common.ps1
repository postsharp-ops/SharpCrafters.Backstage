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
    $repositoryRoot = ( Resolve-Path ( Join-Path $PSScriptRoot '../../..' ) ).Path
    $dockerBuild = Join-Path $repositoryRoot 'DockerBuild.ps1'

    if ($containerOs -eq 'windows')
    {
        # Windows uses the image of the product build: Windows Server 2025 with the .NET 10 SDK and PowerShell 7. A Windows
        # image is several gigabytes, and this one is already in the registry and in the image cache of the agents. The
        # Dockerfile and the context are those of the product build, so DockerBuild.ps1 computes the same content-hash
        # tag and reuses the image instead of building another one. The context directory is created when missing, which
        # the content hash treats as empty, as the product build does.
        $dockerfile = Join-Path $repositoryRoot 'eng/docker/build.Dockerfile'
        $context = Join-Path $repositoryRoot 'eng/docker-context/build'
        New-Item -ItemType Directory -Force $context | Out-Null
    }
    else
    {
        # Linux has no product build image, because the product builds on Windows only.
        $dockerfile = Join-Path $PSScriptRoot 'Images/linux/Dockerfile'
        $context = Split-Path $dockerfile
    }

    # A hashtable, not an array. Splatting an array does not bind the named parameters of DockerBuild.ps1: they fall into
    # its -BuildArgs parameter, and the script runs an ordinary product build instead of the test container.
    $arguments = @{
        Test = $true
        OS = $containerOs
        Dockerfile = $dockerfile
        Context = $context

        # The tests compare what the product detects with the kind of host that this variable declares.
        Env = @( 'BACKSTAGE_PLATFORM_TEST_HOST=container' )

        # The command runs in the mounted repository, through the shell of the image, which is sh or cmd. The SDK images
        # carry PowerShell, so the rest is the same script on every platform.
        Command = "pwsh -NoProfile -File src/tests/Platform/RunSuite.ps1 -Suite $Suite -Run $Suite-$Platform"
    }

    # On Linux the command is made compound, so that sh does not replace itself with pwsh and stays the first process of the
    # container. A process whose parent exits is adopted by the first process, and the build servers that a test starts
    # outlive the build that started them; sh reaps them when they exit, whereas pwsh would leave them as zombies, which a
    # wait for their exit would take for running processes. A bare 'exit' returns the status of pwsh without a '$', which
    # the hop of DockerBuild.ps1 into WSL on a Windows development machine would expand
    # (postsharp-ops/PostSharp.Engineering#167). The Windows command goes through cmd, where ';' does not separate commands.
    if ($containerOs -eq 'linux')
    {
        $arguments.Command += '; exit'
    }

    # Nothing is returned. The output of the container arrives on the standard output of this call, and the caller reads
    # $LASTEXITCODE.
    & $dockerBuild @arguments
}
