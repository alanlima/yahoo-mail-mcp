[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^[a-zA-Z0-9-]{3,24}$')]
    [string] $VaultName,

    [switch] $IncludeOriginProtection
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true

$secretMap = [ordered]@{
    'yahoo-email'       = 'YAHOO_EMAIL'
    'yahoo-app-password' = 'YAHOO_APP_PASSWORD'
    'mcp-bearer-token'  = 'MCP_BEARER_TOKEN'
    'cursor-signing-key' = 'CURSOR_SIGNING_KEY'
}

if ($IncludeOriginProtection) {
    $secretMap['origin-verification-header'] = 'ORIGIN_VERIFICATION_HEADER_VALUE'
}

foreach ($environmentName in $secretMap.Values) {
    $value = [Environment]::GetEnvironmentVariable($environmentName)
    if ([string]::IsNullOrWhiteSpace($value)) {
        throw "Required environment variable $environmentName is missing."
    }

    if ($environmentName -in @(
            'MCP_BEARER_TOKEN',
            'CURSOR_SIGNING_KEY',
            'ORIGIN_VERIFICATION_HEADER_VALUE'
        ) -and
        [Text.Encoding]::UTF8.GetByteCount($value) -lt 32) {
        throw "$environmentName must contain at least 32 UTF-8 bytes."
    }
}

az account show --only-show-errors --output none

try {
    foreach ($entry in $secretMap.GetEnumerator()) {
        if ($PSCmdlet.ShouldProcess("Key Vault $VaultName", "Set secret $($entry.Key)")) {
            Write-Host "Setting secret $($entry.Key) in Key Vault $VaultName"
            $value = [Environment]::GetEnvironmentVariable($entry.Value)
            az keyvault secret set `
                --vault-name $VaultName `
                --name $entry.Key `
                --value $value `
                --only-show-errors `
                --output none
        }
    }

    Write-Host "Provisioned $($secretMap.Count) required secret names in Key Vault '$VaultName'."
}
finally {
    Write-Host "Cleaning up environment variables..."
    foreach ($environmentName in $secretMap.Values) {
        Remove-Item -LiteralPath "Env:$environmentName" -ErrorAction SilentlyContinue
    }
}
