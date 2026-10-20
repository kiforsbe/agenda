#requires -Version 7.4
$ErrorActionPreference='Stop'
$path=Join-Path $PSScriptRoot 'verify.ps1'
if(!(Test-Path -LiteralPath $path)) { throw 'Missing verification entry point.' }
$errors=$null; $tokens=$null
$ast=[Management.Automation.Language.Parser]::ParseFile($path,[ref]$tokens,[ref]$errors)
if($errors) { throw 'Verification script has syntax errors.' }
$params=@($ast.ParamBlock.Parameters | ForEach-Object { $_.Name.VariablePath.UserPath })
if('Configuration' -notin $params -or 'Architecture' -notin $params) { throw 'Verification must accept configuration and architecture.' }
Write-Host 'Verification entry point checks passed.'
