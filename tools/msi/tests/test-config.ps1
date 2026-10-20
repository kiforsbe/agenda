#requires -Version 7.4
$ErrorActionPreference='Stop'
$engine=Split-Path $PSScriptRoot -Parent
if(Test-Path "$engine/Configuration.ps1") { . "$engine/Configuration.ps1"; . "$engine/Identity.ps1"; . "$engine/Payload.ps1" }
if(!(Get-Command Read-MsiConfiguration -ErrorAction SilentlyContinue)) { throw 'Missing configuration validation.' }
$script:checks=0
function Check([bool]$Value,[string]$Message) { if(!$Value) { throw $Message }; $script:checks++ }
function Rejected([scriptblock]$Action) { $failed=$false; try { & $Action | Out-Null } catch { $failed=$true }; Check $failed 'Expected invalid input rejection.' }
$fixture=Join-Path ([IO.Path]::GetTempPath()) ('MsiConfig-'+[guid]::NewGuid().ToString('N'))
$null=New-Item -ItemType Directory -Path "$fixture/payload/Assets" -Force
try {
 $json=Get-Content "$engine/example.json" -Raw
 $path="$fixture/config.json"
 $json | Set-Content $path
 $config=Read-MsiConfiguration $path
 Check ($config.schemaVersion -eq 1) 'Supported schema loads.'
 Push-Location $engine
 try {
  $relative=Read-MsiConfiguration 'example.json'
  Check ($relative.product.id -eq 'example') 'Config paths follow PowerShell current location.'
 } finally { Pop-Location }
 foreach($change in @(
  {param($x) $x.schemaVersion=2},
  {param($x) $x.extra='unknown'},
  {param($x) $x.product.upgradeCode='invalid'},
  {param($x) $x.installation.scope='user'},
  {param($x) $x.application.executable='../app.exe'},
  {param($x) $x.installation.directoryName='CON'},
  {param($x) $x.application.executable='app.exe:stream'},
  {param($x) $x.shortcuts[0].location='desktop'},
  {param($x) $x.product.name=12},
  {param($x) $x.shortcuts=@($x.shortcuts[0],$x.shortcuts[0])}
 )) {
  $obj=$json | ConvertFrom-Json -AsHashtable
  & $change $obj
  $obj | ConvertTo-Json -Depth 10 | Set-Content $path
  Rejected { Read-MsiConfiguration $path }
 }
 $json.Replace('"schemaVersion": 1','"schemaVersion": 1, "schemaVersion": 1') | Set-Content $path
 Rejected { Read-MsiConfiguration $path }
 $json | Set-Content $path
 foreach($v in @('0.1.0','255.255.65534')) { Assert-MsiVersion $v; $script:checks++ }
 foreach($v in @('256.0.0','0.256.0','0.0.65535','01.0.0','1.0.0-preview','1.0.0.0')) { Rejected { Assert-MsiVersion $v } }
 $a=Get-MsiGuid $config.product.upgradeCode 'component/x64/app.exe'
 Check ($a -eq (Get-MsiGuid $config.product.upgradeCode 'COMPONENT/X64/APP.EXE')) 'Identities ignore path case.'
 Check ($a -ne (Get-MsiGuid $config.product.upgradeCode 'component/arm64/app.exe')) 'Architectures isolated.'
 Check ($a -ne (Get-MsiGuid ([guid]::NewGuid()) 'component/x64/app.exe')) 'Products isolated.'
 Rejected { Get-MsiPayload $config "$fixture/payload" 'x64' }
 [IO.File]::WriteAllText("$fixture/payload/Example.exe",'synthetic')
 [IO.File]::WriteAllText("$fixture/payload/Assets/Example.ico",'synthetic')
 $files=@(Get-MsiPayload $config "$fixture/payload" 'x64')
 Check ($files.Count -eq 2) 'Approved payload enumerated.'
 [IO.File]::WriteAllText("$fixture/payload/Assets/settings.json",'synthetic approved data')
 $config.payload.allowedFiles=@('Assets\settings.json')
 Check (@(Get-MsiPayload $config "$fixture/payload" 'x64').Count -eq 3) 'Backslash exact approvals match normalized payload paths.'
 Remove-Item "$fixture/payload/Assets/settings.json"
 [IO.File]::WriteAllText("$fixture/payload/credentials.json",'secret fixture')
 Rejected { Get-MsiPayload $config "$fixture/payload" 'x64' }
 Remove-Item "$fixture/payload/credentials.json"
 $junction="$fixture/linked"
 $null=New-Item -ItemType Junction -Path $junction -Target "$fixture/payload"
 Rejected { Get-MsiPayload $config $junction 'x64' }
 Remove-Item -LiteralPath $junction -Force
 Write-Host "Configuration checks passed: $checks"
} finally {
 $resolved=[IO.Path]::GetFullPath($fixture)
 if([IO.Path]::GetDirectoryName($resolved) -ne [IO.Path]::GetTempPath().TrimEnd('\') -or [IO.Path]::GetFileName($resolved) -notlike 'MsiConfig-*') { throw 'Unsafe test cleanup.' }
 Remove-Item -LiteralPath $resolved -Recurse -Force
}
