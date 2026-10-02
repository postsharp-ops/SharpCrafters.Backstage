# Prepares the test archive of the platform tests on a test agent. eng/RunTests.ps1 runs this script in the directory of the
# extracted archive, before it starts the tests. See doc/testing-platform.md in PostSharp.Engineering.
#
# The script needs PowerShell only: the test agents have no .NET SDK.

param(
    # The directory under which the build artifacts were downloaded. The platform tests read none.
    [Parameter( Mandatory = $true )] [string] $RepositoryRoot,

    # The directory of the extracted archive.
    [Parameter( Mandatory = $true )] [string] $ApplicationDirectory
)

$ErrorActionPreference = 'Stop'

# The build configuration of each test agent declares the kind of host, because detecting it is part of the code under test.
# Without it, the tests that depend on the kind of host would be skipped, and the run would pass without running them.
$hostVariable = 'BACKSTAGE_PLATFORM_TEST_HOST'
$declaredHost = [Environment]::GetEnvironmentVariable( $hostVariable )

if ($declaredHost -notin @( 'container', 'host' ))
{
    throw "The environment variable $hostVariable must be 'container' or 'host', but it is '$declaredHost'. The build configuration of the test agent sets it."
}

if ($IsLinux -or $IsMacOS)
{
    # The application host of the helper for this platform. The build writes one per Unix runtime, because the one beside the
    # helper assembly is for Windows, where the build runs. The application host finds the helper assembly in its own
    # directory, so it is copied there.
    $os = if ($IsLinux) { 'linux' } else { 'osx' }
    $architecture = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString().ToLowerInvariant()
    $runtime = "$os-$architecture"
    $name = 'SharpCrafters.Backstage.PlatformTestHelper'

    $source = Join-Path $ApplicationDirectory "apphosts/$runtime/$name"

    if (-not ( Test-Path $source ))
    {
        throw "The archive has no application host of the helper for '$runtime'. Add the runtime to PlatformTestHelperRuntime in SharpCrafters.Backstage.PlatformTests.csproj."
    }

    $target = Join-Path $ApplicationDirectory $name
    Copy-Item $source $target -Force

    # A zip file does not keep the permissions of a file.
    & chmod +x $target

    if ($LASTEXITCODE -ne 0)
    {
        throw "chmod failed with exit code $LASTEXITCODE."
    }

    # macOS on ARM64 runs only a signed executable. The build cannot sign on Windows, so the application host is signed
    # here, with an ad hoc signature, as the .NET SDK does on macOS.
    if ($IsMacOS)
    {
        & codesign --sign - --force $target

        if ($LASTEXITCODE -ne 0)
        {
            throw "codesign failed with exit code $LASTEXITCODE."
        }
    }

    Write-Host "Copied the application host of the helper for '$runtime'."
}

# No environment variable to add.
return @{ }
