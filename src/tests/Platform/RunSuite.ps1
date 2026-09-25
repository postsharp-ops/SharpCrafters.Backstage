# Runs the platform tests of one suite, or of all suites, where this script runs: inside a test container, or on a macOS
# agent. It builds the tests against the packages of the build and runs them with `dotnet test`.
#
# It is not the launcher of the Docker tests. eng/RunDockerTests.ps1 runs Docker/<Suite>/RunTest.ps1 on the host, which
# starts a container that runs this script.

param(
    # The suite, which is a namespace of PlatformTests, for example 'NamedLocks'. All suites when omitted.
    [string] $Suite,

    # The name of the run, which separates its build output from the output of the other runs that share the repository.
    [Parameter( Mandatory = $true )] [string] $Run
)

$ErrorActionPreference = 'Stop'

$repositoryRoot = ( Resolve-Path ( Join-Path $PSScriptRoot '../../..' ) ).Path

# The NuGet configuration whose source is the directory of the packages of the build. On Linux, nuget.wsl.config names it by
# the path that a container started from WSL sees. On a build agent, both files are the same copy of nuget.restored.config,
# whose paths are relative. Without either, a restore would fall back to nuget.org and test a published version.
$configFile = Join-Path $repositoryRoot 'nuget.config'
$wslConfigFile = Join-Path $repositoryRoot 'nuget.wsl.config'

if ($IsLinux -and ( Test-Path $wslConfigFile ))
{
    $configFile = $wslConfigFile
}

if (-not ( Test-Path $configFile ))
{
    throw "The NuGet configuration '$configFile' is missing. Run 'Build.ps1 build', or restore the artifacts of the build."
}

$arguments = @(
    'test', ( Join-Path $PSScriptRoot 'PlatformTests/PlatformTests.csproj' ),
    "-p:RestoreConfigFile=$configFile",
    "-p:PlatformTestsRun=$Run",
    '--disable-build-servers',
    '--logger', 'console;verbosity=normal',
    '--results-directory', ( Join-Path $PSScriptRoot ".artifacts/$Run/results" ) )

if ($Suite)
{
    # HostKindTests runs with every suite, so that a launcher that does not declare the kind of host fails.
    $arguments += @( '--filter', "FullyQualifiedName~.$Suite.|FullyQualifiedName~.HostKindTests." )
}

Write-Host "dotnet $( $arguments -join ' ' )"

# The muxer resolves global.json from the working directory, not from the project. The working directory is the repository
# root, whose global.json pins an SDK and the PostSharp.Engineering SDK that a test image does not carry.
Push-Location $PSScriptRoot

try
{
    & dotnet @arguments
}
finally
{
    Pop-Location
}

exit $LASTEXITCODE
