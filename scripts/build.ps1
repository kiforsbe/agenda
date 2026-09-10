param([ValidateSet('Debug','Release')][string]$Configuration='Debug',[ValidateSet('x64','ARM64')][string]$Architecture='x64')
$ErrorActionPreference='Stop'
$repoRoot=Split-Path $PSScriptRoot -Parent
Push-Location $repoRoot
try {
    $env:DOTNET_CLI_HOME=Join-Path $repoRoot '.dotnet'
    $env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH='false'
    $runtimeId='win-'+$Architecture.ToLowerInvariant()
    dotnet restore src/Agenda.App --configfile NuGet.Config -p:Platform=$Architecture -r $runtimeId
    if ($LASTEXITCODE -ne 0) { throw 'Dependency restore failed.' }
    dotnet build src/Agenda.App --no-restore -c $Configuration -p:Platform=$Architecture -r $runtimeId
    if ($LASTEXITCODE -ne 0) { throw 'Native application build failed.' }
} finally { Pop-Location }
