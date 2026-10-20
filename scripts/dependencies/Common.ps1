#requires -Version 7.4
function Get-AgendaDependencyProjects {
    $root=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
    Get-ChildItem (Join-Path $root 'src'),(Join-Path $root 'tests') -Filter '*.csproj' -Recurse -File | ForEach-Object FullName
}
function Get-AgendaDependencyReport {
    param([string]$Project,[ValidateSet('outdated','deprecated','vulnerable')][string]$Kind)
    $previousNoLogo=$env:DOTNET_NOLOGO
    try {
        # A fresh SDK can print its welcome banner before JSON on first use.
        $env:DOTNET_NOLOGO='true'
        $output=@(& dotnet list $Project package "--$Kind" --include-transitive --format json --no-restore 2>&1)
        $exitCode=$LASTEXITCODE
    } finally { $env:DOTNET_NOLOGO=$previousNoLogo }
    if($exitCode -ne 0) { throw "Dependency $Kind query failed for $Project. $($output -join [Environment]::NewLine)" }
    $report=($output -join [Environment]::NewLine) | ConvertFrom-Json -AsHashtable
    if($report.problems) { throw "Dependency $Kind query returned problems: $($report.problems | ConvertTo-Json -Compress)" }
    return $report
}
function Assert-AgendaVulnerabilityReport {
    param([System.Collections.IDictionary]$Report)
    if($Report.version -ne 1 -or !$Report.projects -or $Report.problems) { throw 'NuGet advisory report is incomplete or unsupported.' }
    foreach($project in $Report.projects) {
        # NuGet omits frameworks when this project has no advisory findings.
        if(!$project.path) { throw 'NuGet advisory report has no project identity.' }
        foreach($framework in $project.frameworks) {
            if($framework.topLevelPackages -or $framework.transitivePackages) { throw 'Known-vulnerable dependencies reported; review the complete dependency graph.' }
        }
    }
}
function Get-AgendaAuditArguments {
    @('--force','-p:NuGetAudit=true','-p:NuGetAuditMode=all','-p:NuGetAuditLevel=low',
      '-p:WarningsAsErrors=NU1900%3BNU1901%3BNU1902%3BNU1903%3BNU1904%3BNU1905','-p:RestoreIgnoreFailedSources=false')
}
