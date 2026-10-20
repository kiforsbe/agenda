param([ValidateSet('Debug','Release')][string]$Configuration='Debug')
$ErrorActionPreference='Stop'
$repoRoot=Split-Path $PSScriptRoot -Parent
Push-Location $repoRoot
$previousHome=$env:DOTNET_CLI_HOME
try {
    $env:DOTNET_CLI_HOME=Join-Path $repoRoot '.dotnet'
    foreach ($project in @('tests/Agenda.Tests','tests/Agenda.ProviderTests','tests/Agenda.ShellTests')) {
        dotnet restore $project --locked-mode --configfile NuGet.Config
        if ($LASTEXITCODE -ne 0) { throw "Restore failed: $project" }
        dotnet run --project $project --no-restore -c $Configuration
        if ($LASTEXITCODE -ne 0) { throw "Tests failed: $project" }
    }
} finally { $env:DOTNET_CLI_HOME=$previousHome; Pop-Location }
