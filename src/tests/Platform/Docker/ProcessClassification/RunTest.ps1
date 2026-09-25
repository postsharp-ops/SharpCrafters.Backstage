param( [Parameter( Mandatory = $true )] [string] $Platform )

. ( Join-Path $PSScriptRoot '../../Common.ps1' )

Invoke-PlatformTestSuite -Platform $Platform -Suite 'ProcessClassification'

exit $LASTEXITCODE
