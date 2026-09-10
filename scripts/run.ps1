param([switch]$Build,[ValidateSet('Debug','Release')][string]$Configuration='Debug',[string]$DataDirectory)
$ErrorActionPreference='Stop'
$repoRoot=Split-Path $PSScriptRoot -Parent
if ($Build) { & (Join-Path $PSScriptRoot 'build.ps1') -Configuration $Configuration }
$executable=Join-Path $repoRoot "src/Agenda.App/bin/x64/$Configuration/net10.0-windows10.0.26100.0/win-x64/Agenda.App.exe"
if (-not (Test-Path -LiteralPath $executable)) { throw 'Build not found. Run scripts/build.ps1 first.' }
$launch=@{ FilePath=$executable; WorkingDirectory=(Split-Path $executable); WindowStyle='Hidden' }
if ($DataDirectory) { $launch.ArgumentList=@('--data-dir', ('"'+[IO.Path]::GetFullPath($DataDirectory)+'"')) }
Start-Process @launch
