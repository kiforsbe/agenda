$ErrorActionPreference='Stop'
$repoRoot=Split-Path $PSScriptRoot -Parent
Push-Location $repoRoot
try {
    $env:DOTNET_CLI_HOME=Join-Path $repoRoot '.dotnet'
    foreach ($project in @('tests/Agenda.Tests','tests/Agenda.ProviderTests','tests/Agenda.ShellTests')) {
        dotnet restore $project --configfile NuGet.Config
        if ($LASTEXITCODE -ne 0) { throw "Restore failed: $project" }
        dotnet run --project $project --no-restore
        if ($LASTEXITCODE -ne 0) { throw "Tests failed: $project" }
    }
} finally { Pop-Location }
