[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$sourceRoot = Join-Path $PSScriptRoot '..\src'
$forbiddenPatterns = @(
    'MessageFlags[.]Deleted',
    '[.]Expunge(?:Async)?\s*[(]',
    '[.]Delete(?:Async)?\s*[(]',
    '(?:AddFlags|SetFlags)(?:Async)?\s*[(][^\r\n]*Deleted',
    'UID\s+EXPUNGE',
    'STORE[^\r\n]*\\Deleted'
)

$violations = foreach ($file in Get-ChildItem -LiteralPath $sourceRoot -Filter '*.cs' -Recurse -File) {
    $lineNumber = 0
    foreach ($line in Get-Content -LiteralPath $file.FullName) {
        $lineNumber++
        foreach ($pattern in $forbiddenPatterns) {
            if ($line -match $pattern) {
                [pscustomobject]@{
                    File = $file.FullName
                    Line = $lineNumber
                    Pattern = $pattern
                }
            }
        }
    }
}

if ($violations) {
    $violations | Format-Table -AutoSize | Out-String | Write-Error
    exit 1
}

Write-Host 'No forbidden mail mutation APIs were found in production C# sources.'
