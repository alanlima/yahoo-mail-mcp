[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory)] [guid] $ClientId,
    [Parameter(Mandatory)] [ValidatePattern('^https://')] [uri] $RedirectUri
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true

$application = az rest `
    --method get `
    --url "https://graph.microsoft.com/v1.0/applications?`$filter=appId eq '$ClientId'&`$select=id,web" `
    --query 'value[0]' `
    --output json | ConvertFrom-Json
if (-not $application) {
    throw "Entra application '$ClientId' was not found."
}

$redirectUris = @($application.web.redirectUris) + $RedirectUri.AbsoluteUri | Sort-Object -Unique
if ($PSCmdlet.ShouldProcess($ClientId, "Add redirect URI $RedirectUri")) {
    $body = @{ web = @{ redirectUris = $redirectUris } } | ConvertTo-Json -Depth 5 -Compress
    $temporaryFile = [IO.Path]::GetTempFileName()
    try {
        [IO.File]::WriteAllText($temporaryFile, $body, [Text.UTF8Encoding]::new($false))
        az rest `
            --method patch `
            --url "https://graph.microsoft.com/v1.0/applications/$($application.id)" `
            --headers 'Content-Type=application/json' `
            --body "@$temporaryFile" `
            --output none
    }
    finally {
        Remove-Item -LiteralPath $temporaryFile -Force -ErrorAction SilentlyContinue
    }
    Write-Host 'Added the redirect URI without changing existing client credentials.'
}
