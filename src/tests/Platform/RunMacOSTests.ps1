# Runs every platform test suite on the macOS agent itself, because no container engine provides a macOS container. The
# TeamCity configuration 'Platform Tests (macOS ARM64)' runs this script after restoring the artifacts of the Debug build.
#
# The host needs PowerShell 7 only. The script installs the .NET SDK into the artifacts directory of the tests, and it
# points HOME at a directory of its own, which holds the state of the .NET SDK. It does not isolate the data directory of
# the product: on macOS, the runtime resolves ~/Library/Application Support from the account of the user rather than
# from HOME.

$ErrorActionPreference = 'Stop'

if (-not $IsMacOS)
{
    throw 'This script runs the platform tests on macOS. Linux and Windows run them in containers, through eng/RunDockerTests.ps1.'
}

$artifactsDirectory = Join-Path $PSScriptRoot '.artifacts'
$dotNetDirectory = Join-Path $artifactsDirectory 'dotnet'
$dotNetPath = Join-Path $dotNetDirectory 'dotnet'

if (-not ( Test-Path $dotNetPath ))
{
    # The major version of the SDK that global.json asks for. It rolls forward, so the latest SDK of that version satisfies it.
    $sdkVersion = ( Get-Content ( Join-Path $PSScriptRoot 'global.json' ) -Raw | ConvertFrom-Json ).sdk.version
    $channel = ( $sdkVersion -split '\.' )[0..1] -join '.'

    New-Item -ItemType Directory -Force $artifactsDirectory | Out-Null
    $installScript = Join-Path $artifactsDirectory 'dotnet-install.sh'
    Invoke-WebRequest -Uri 'https://dot.net/v1/dotnet-install.sh' -OutFile $installScript

    & bash $installScript --channel $channel --install-dir $dotNetDirectory --no-path

    if ($LASTEXITCODE -ne 0)
    {
        throw "The installation of the .NET SDK $channel failed with exit code $LASTEXITCODE."
    }
}

# The NuGet cache stays the one of the agent, so that the packages that the tests restore from nuget.org are downloaded once.
if (-not $env:NUGET_PACKAGES)
{
    $env:NUGET_PACKAGES = Join-Path $HOME '.nuget/packages'
}

$testHome = Join-Path $artifactsDirectory 'home'
Remove-Item -Recurse -Force $testHome -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $testHome | Out-Null

$env:HOME = $testHome
$env:DOTNET_ROOT = $dotNetDirectory
$env:PATH = "$dotNetDirectory$( [IO.Path]::PathSeparator )$env:PATH"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$env:BACKSTAGE_PLATFORM_TEST_HOST = 'host'

& ( Join-Path $PSScriptRoot 'RunSuite.ps1' ) -Run 'macos-arm64'

exit $LASTEXITCODE
