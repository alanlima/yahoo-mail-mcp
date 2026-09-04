[CmdletBinding(SupportsShouldProcess)]
param(
    [string] $DisplayName = 'Yahoo Mail MCP - ChatGPT',
    [Parameter(Mandatory)] [ValidatePattern('^https://')] [uri] $ResourceUri,
    [Parameter(Mandatory)] [ValidatePattern('^https://')] [uri] $ChatGptRedirectUri,
    [Parameter(Mandatory)] [string] $VaultName,
    [ValidateRange(1, 730)] [int] $SecretLifetimeDays = 180
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true

function Invoke-GraphJsonRequest {
    param(
        [Parameter(Mandatory)] [string] $Method,
        [Parameter(Mandatory)] [string] $Url,
        [Parameter(Mandatory)] [string] $Json,
        [switch] $NoOutput
    )

    $temporaryFile = [IO.Path]::GetTempFileName()
    try {
        [IO.File]::WriteAllText($temporaryFile, $Json, [Text.UTF8Encoding]::new($false))
        $outputFormat = $NoOutput ? 'none' : 'json'
        az rest `
            --method $Method `
            --url $Url `
            --headers 'Content-Type=application/json' `
            --body "@$temporaryFile" `
            --output $outputFormat
    }
    finally {
        Remove-Item -LiteralPath $temporaryFile -Force -ErrorAction SilentlyContinue
    }
}

az account show --only-show-errors --output none
$tenantId = az account show --query tenantId --output tsv
$existing = az rest `
    --method get `
    --url "https://graph.microsoft.com/v1.0/applications?`$filter=displayName eq '$DisplayName'&`$select=id,appId,displayName" `
    --headers ConsistencyLevel=eventual `
    --query 'value[0]' `
    --output json | ConvertFrom-Json
if ($existing) {
    throw "An Entra application named '$DisplayName' already exists. Review it instead of creating duplicate credentials."
}

if (-not $PSCmdlet.ShouldProcess($DisplayName, 'Create Entra app registration, client secret, service principal, and Key Vault entries')) {
    return
}

$scopeId = [guid]::NewGuid().ToString()
$createdObjectId = $null
$clientSecret = $null
try {
    $applicationBody = @{
        displayName    = $DisplayName
        signInAudience = 'AzureADMyOrg'
        identifierUris = @($ResourceUri.AbsoluteUri.TrimEnd('/'))
        api            = @{
            requestedAccessTokenVersion = 2
            oauth2PermissionScopes       = @(
                @{
                    id                         = $scopeId
                    value                      = 'access_as_user'
                    type                       = 'User'
                    isEnabled                  = $true
                    adminConsentDisplayName    = 'Access Yahoo Mail MCP'
                    adminConsentDescription    = 'Allows ChatGPT to access the single-user Yahoo Mail MCP server on behalf of the signed-in user.'
                    userConsentDisplayName     = 'Access Yahoo Mail MCP'
                    userConsentDescription     = 'Allow ChatGPT to access your Yahoo Mail MCP server.'
                }
            )
        }
        web            = @{
            redirectUris         = @($ChatGptRedirectUri.AbsoluteUri)
            implicitGrantSettings = @{
                enableAccessTokenIssuance = $false
                enableIdTokenIssuance     = $false
            }
        }
    } | ConvertTo-Json -Depth 10 -Compress

    $application = Invoke-GraphJsonRequest `
        -Method post `
        -Url 'https://graph.microsoft.com/v1.0/applications' `
        -Json $applicationBody | ConvertFrom-Json
    $createdObjectId = $application.id

    $requiredAccess = @{
        requiredResourceAccess = @(
            @{
                resourceAppId  = $application.appId
                resourceAccess = @(@{ id = $scopeId; type = 'Scope' })
            }
        )
    } | ConvertTo-Json -Depth 8 -Compress
    Invoke-GraphJsonRequest `
        -Method patch `
        -Url "https://graph.microsoft.com/v1.0/applications/$createdObjectId" `
        -Json $requiredAccess `
        -NoOutput

    $servicePrincipalBody = @{ appId = $application.appId } | ConvertTo-Json -Compress
    Invoke-GraphJsonRequest `
        -Method post `
        -Url 'https://graph.microsoft.com/v1.0/servicePrincipals' `
        -Json $servicePrincipalBody `
        -NoOutput

    $secretBody = @{
        passwordCredential = @{
            displayName = 'ChatGPT MCP client secret'
            endDateTime = [DateTimeOffset]::UtcNow.AddDays($SecretLifetimeDays).ToString('O')
        }
    } | ConvertTo-Json -Depth 5 -Compress
    $credential = Invoke-GraphJsonRequest `
        -Method post `
        -Url "https://graph.microsoft.com/v1.0/applications/$createdObjectId/addPassword" `
        -Json $secretBody | ConvertFrom-Json
    $clientSecret = $credential.secretText

    az keyvault secret set --vault-name $VaultName --name mcp-oauth-client-id --value $application.appId --only-show-errors --output none
    az keyvault secret set --vault-name $VaultName --name mcp-oauth-client-secret --value $clientSecret --expires $credential.endDateTime --only-show-errors --output none
    az keyvault secret set --vault-name $VaultName --name mcp-oauth-tenant-id --value $tenantId --only-show-errors --output none

    Write-Host "Created Entra application '$DisplayName' and stored mcp-oauth-client-id, mcp-oauth-client-secret, and mcp-oauth-tenant-id in Key Vault '$VaultName'."
    Write-Host "Resource: $($ResourceUri.AbsoluteUri.TrimEnd('/'))"
    Write-Host "Redirect URI: $($ChatGptRedirectUri.AbsoluteUri)"
}
catch {
    if ($createdObjectId) {
        az rest --method delete --url "https://graph.microsoft.com/v1.0/applications/$createdObjectId" --output none 2>$null
    }

    throw
}
finally {
    $clientSecret = $null
}
