#requires -Version 7.4
[CmdletBinding()]
param([ValidateSet('Debug','Release')][string]$Configuration='Release',[ValidateSet('x64','ARM64')][string]$Architecture='x64')
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    foreach($script in @('tools/msi/tests/test-config.ps1','tools/msi/tests/test-msi.ps1','scripts/test-dependencies.ps1','scripts/test-packaging.ps1','scripts/test-verification.ps1')) {
        & (Join-Path $root $script)
        if(!$?) { throw "Verification failed: $script" }
    }
    & (Join-Path $PSScriptRoot 'test.ps1') -Configuration $Configuration
    if(!$?) { throw 'Agenda tests failed.' }
    & (Join-Path $PSScriptRoot 'build.ps1') -Configuration $Configuration -Architecture $Architecture
    if(!$?) { throw 'Agenda build failed.' }
    foreach($arguments in @(@('diff','--check'),@('diff','--cached','--check'))) {
        & git @arguments
        if($LASTEXITCODE -ne 0) { throw 'Git whitespace check failed.' }
    }
} finally { Pop-Location }
