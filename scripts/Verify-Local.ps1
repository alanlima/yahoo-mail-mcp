[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
$repoRoot = Split-Path -Parent $PSScriptRoot
Push-Location $repoRoot
try {
    dotnet restore --locked-mode
    dotnet build --configuration Release --no-restore
    dotnet test --configuration Release --no-build
    dotnet format --verify-no-changes
    & (Join-Path $PSScriptRoot 'Check-NoDelete.ps1')
}
finally {
    Pop-Location
}
