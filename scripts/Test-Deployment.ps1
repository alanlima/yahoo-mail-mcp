[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^https://')]
    [uri] $BaseUri,

    [ValidatePattern('^https://')]
    [uri] $HealthBaseUri = $BaseUri,

    [Alias('BearerToken')]
    [string] $ApiKey = $env:MCP_BEARER_TOKEN,

    [switch] $ExpectOAuth
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($ApiKey)) {
    throw 'Supply -ApiKey or set MCP_BEARER_TOKEN.'
}

$expectedTools = @(
    'yahoo_mail_get_message',
    'yahoo_mail_list_folders',
    'yahoo_mail_list_messages',
    'yahoo_mail_mark_read',
    'yahoo_mail_mark_unread',
    'yahoo_mail_move_message',
    'yahoo_mail_search_messages',
    'yahoo_mail_set_flagged',
    'yahoo_mail_status'
)

function Invoke-McpJsonRequest {
    param(
        [Parameter(Mandatory)] [hashtable] $Body
    )

    $mcpUri = [uri]::new($BaseUri, '/mcp')
    $response = Invoke-WebRequest `
        -Uri $mcpUri `
        -Method Post `
        -Headers @{
            Authorization = "ApiKey $ApiKey"
            Accept        = 'application/json, text/event-stream'
        } `
        -ContentType 'application/json' `
        -Body ($Body | ConvertTo-Json -Depth 10 -Compress)

    $content = $response.Content
    if ($response.Headers.'Content-Type' -like 'text/event-stream*') {
        $dataLine = $content -split "`n" | Where-Object { $_ -like 'data:*' } | Select-Object -Last 1
        if (-not $dataLine) {
            throw 'The MCP server returned an empty event stream.'
        }
        $content = $dataLine.Substring(5).Trim()
    }

    return $content | ConvertFrom-Json
}

Invoke-WebRequest -Uri ([uri]::new($HealthBaseUri, '/health/live')) -UseBasicParsing | Out-Null
Invoke-WebRequest -Uri ([uri]::new($HealthBaseUri, '/health/ready')) -UseBasicParsing | Out-Null

$unauthorized = Invoke-WebRequest `
    -Uri ([uri]::new($BaseUri, '/mcp')) `
    -Method Post `
    -ContentType 'application/json' `
    -Body '{"jsonrpc":"2.0","id":0,"method":"tools/list","params":{}}' `
    -SkipHttpErrorCheck
if ($unauthorized.StatusCode -ne 401) {
    throw "An unauthenticated MCP request returned HTTP $($unauthorized.StatusCode), expected 401."
}

if ($ExpectOAuth) {
    $challenge = [string]::Join(',', @($unauthorized.Headers.'WWW-Authenticate'))
    if ($challenge -notmatch 'resource_metadata=') {
        throw 'The MCP 401 challenge did not advertise OAuth protected-resource metadata.'
    }

    $metadataResponse = Invoke-WebRequest `
        -Uri ([uri]::new($BaseUri, '/.well-known/oauth-protected-resource')) `
        -UseBasicParsing
    $metadata = $metadataResponse.Content | ConvertFrom-Json
    $expectedResource = [uri]::new($BaseUri, '/mcp').AbsoluteUri
    if ($metadata.resource -ne $expectedResource) {
        throw "OAuth metadata resource '$($metadata.resource)' did not match '$expectedResource'."
    }

    if (-not @($metadata.scopes_supported).Where({ $_ -eq "$expectedResource/access_as_user" })) {
        throw 'OAuth metadata did not advertise the qualified access_as_user scope.'
    }

    if ($metadataResponse.Content -match 'client_secret') {
        throw 'OAuth metadata unexpectedly contained a client-secret field.'
    }
}

$initialize = Invoke-McpJsonRequest -Body @{
    jsonrpc = '2.0'
    id      = 1
    method  = 'initialize'
    params  = @{
        protocolVersion = '2025-06-18'
        capabilities    = @{}
        clientInfo      = @{ name = 'deployment-smoke-test'; version = '1.0.0' }
    }
}
if (-not $initialize.result) {
    throw 'MCP initialization did not return a result.'
}

$toolResponse = Invoke-McpJsonRequest -Body @{
    jsonrpc = '2.0'
    id      = 2
    method  = 'tools/list'
    params  = @{}
}
$actualTools = @($toolResponse.result.tools.name | Sort-Object)
$difference = Compare-Object -ReferenceObject $expectedTools -DifferenceObject $actualTools
if ($difference) {
    throw "Unexpected MCP tool surface: $($difference | Out-String)"
}

Write-Host 'Deployment health, authentication, OAuth metadata (when requested), initialization, and exact MCP tool discovery passed.'
