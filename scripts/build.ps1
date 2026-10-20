param([ValidateSet('Debug','Release')][string]$Configuration='Debug',[ValidateSet('x64','ARM64')][string]$Architecture='x64')
$ErrorActionPreference='Stop'
$repoRoot=Split-Path $PSScriptRoot -Parent
Push-Location $repoRoot
$previousHome=$env:DOTNET_CLI_HOME
$previousTools=$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH
try {
    $env:DOTNET_CLI_HOME=Join-Path $repoRoot '.dotnet'
    $env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH='false'
    $runtimeId='win-'+$Architecture.ToLowerInvariant()
    # --runtime narrows RuntimeIdentifiers and invalidates the shared two-RID
    # lock file. Set the selected RID property while preserving the reviewed set.
    dotnet restore src/Agenda.App --locked-mode --configfile NuGet.Config -p:Platform=$Architecture "-p:RuntimeIdentifier=$runtimeId"
    if ($LASTEXITCODE -ne 0) { throw 'Dependency restore failed.' }
    dotnet build src/Agenda.App --no-restore -c $Configuration -p:Platform=$Architecture "-p:RuntimeIdentifier=$runtimeId"
    if ($LASTEXITCODE -ne 0) { throw 'Native application build failed.' }
} finally { $env:DOTNET_CLI_HOME=$previousHome; $env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH=$previousTools; Pop-Location }
