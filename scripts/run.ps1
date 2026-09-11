param([switch]$Build,[ValidateSet('Debug','Release')][string]$Configuration='Debug',[string]$DataDirectory)
$ErrorActionPreference='Stop'
$repoRoot=Split-Path $PSScriptRoot -Parent
# Always run the incremental build so MSBuild can detect missing or stale outputs.
# Keep -Build accepted for compatibility with existing commands.
& (Join-Path $PSScriptRoot 'build.ps1') -Configuration $Configuration
$executable=Join-Path $repoRoot "src/Agenda.App/bin/x64/$Configuration/net10.0-windows10.0.26100.0/win-x64/Agenda.App.exe"
if (-not (Test-Path -LiteralPath $executable)) { throw 'Build completed without producing the Agenda executable.' }
$launch=@{ FilePath=$executable; WorkingDirectory=(Split-Path $executable); WindowStyle='Hidden' }
if ($DataDirectory) { $launch.ArgumentList=@('--data-dir', ('"'+[IO.Path]::GetFullPath($DataDirectory)+'"')) }
Start-Process @launch
