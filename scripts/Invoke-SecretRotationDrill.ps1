[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory)] [string] $VaultName,
    [Parameter(Mandatory)] [string] $ResourceGroup,
    [Parameter(Mandatory)] [string] $ContainerAppName,
    [Parameter(Mandatory)] [ValidatePattern('^https://')] [uri] $BaseUri
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true

if (-not $PSCmdlet.ShouldProcess($ContainerAppName, 'Rotate Key Vault secrets and restart the active revision')) {
    return
}

$apiKey = $env:MCP_BEARER_TOKEN
& "$PSScriptRoot/Set-KeyVaultSecrets.ps1" -VaultName $VaultName -Confirm:$false

$revision = az containerapp revision list `
    --name $ContainerAppName `
    --resource-group $ResourceGroup `
    --query "[?properties.active].name | [0]" `
    --output tsv `
    --only-show-errors
if ([string]::IsNullOrWhiteSpace($revision)) {
    throw 'No active Container Apps revision was found.'
}

az containerapp revision restart `
    --name $ContainerAppName `
    --resource-group $ResourceGroup `
    --revision $revision `
    --only-show-errors `
    --output none

& "$PSScriptRoot/Test-Deployment.ps1" -BaseUri $BaseUri -ApiKey $apiKey
$apiKey = $null
Write-Host 'Secret rotation drill completed. Record the date, operator, revision, and smoke-test result without secret values.'
