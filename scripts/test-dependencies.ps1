#requires -Version 7.4
$ErrorActionPreference='Stop'
$helper=Join-Path $PSScriptRoot 'dependencies/Common.ps1'
if(Test-Path $helper) { . $helper }
if(!(Get-Command Assert-AgendaVulnerabilityReport -ErrorAction SilentlyContinue)) { throw 'Missing dependency report validation.' }
function Rejected([scriptblock]$Action) { $failed=$false; try { & $Action } catch { $failed=$true }; if(!$failed) { throw 'Expected incomplete/advisory report rejection.' } }
Assert-AgendaVulnerabilityReport @{version=1;projects=@(@{path='app';frameworks=@(@{framework='net10.0'})})}
Assert-AgendaVulnerabilityReport @{version=1;projects=@(@{path='app'})}
Rejected { Assert-AgendaVulnerabilityReport @{version=1;projects=@()} }
Rejected { Assert-AgendaVulnerabilityReport @{version=1;projects=@(@{path='app';frameworks=@(@{transitivePackages=@(@{id='unsafe';vulnerabilities=@(@{severity='Low'})})})})} }
Rejected { Assert-AgendaVulnerabilityReport @{version=1;projects=@(@{path='app';frameworks=@(@{topLevelPackages=@(@{id='unsafe'})})})} }
Rejected { Assert-AgendaVulnerabilityReport @{version=1;projects=@(@{path='app'});problems=@(@{level='Warning';text='Feed unavailable'})} }
# A metadata/report query must never restore or mutate development intermediates.
function dotnet {
    if('--no-restore' -notin $args) { throw 'Dependency queries must preserve the existing restore graph.' }
    $global:LASTEXITCODE=0
    '{"version":1,"projects":[{"path":"synthetic"}]}'
}
$null=Get-AgendaDependencyReport -Project synthetic -Kind vulnerable
Write-Host 'Dependency report checks passed.'
