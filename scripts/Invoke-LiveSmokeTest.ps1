[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$required = @('YAHOO__EMAIL', 'YAHOO__APPPASSWORD')
foreach ($name in $required) {
    if ([string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($name))) {
        throw "Set $name before running the opt-in live Yahoo smoke test."
    }
}

$createdCursorKey = [string]::IsNullOrWhiteSpace($env:CURSOR__SIGNINGKEY)
if ($createdCursorKey) {
    $env:CURSOR__SIGNINGKEY = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
}

$env:RUN_LIVE_YAHOO_TESTS = 'true'
try {
    dotnet test `
        ./tests/YahooMailMcp.IntegrationTests/YahooMailMcp.IntegrationTests.csproj `
        --configuration Release `
        --filter 'Category=LiveYahoo'
}
finally {
    Remove-Item Env:RUN_LIVE_YAHOO_TESTS -ErrorAction SilentlyContinue
    if ($createdCursorKey) {
        Remove-Item Env:CURSOR__SIGNINGKEY -ErrorAction SilentlyContinue
    }
}
