#requires -Version 7.4
# Refresh evidence only. This does not approve licenses or change dependency pins.
[CmdletBinding()]
param([string]$OutputPath)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'dependencies/Common.ps1')
$root=Split-Path $PSScriptRoot -Parent
if(!$OutputPath) { $OutputPath=Join-Path $root 'docs/dependency-inventory.json' }
$pairs=@{}
$reports=@()
foreach($project in Get-AgendaDependencyProjects) {
    foreach($kind in @('outdated','deprecated','vulnerable')) {
        $report=Get-AgendaDependencyReport $project $kind
        if($kind -eq 'vulnerable') { Assert-AgendaVulnerabilityReport $report }
        # Store repository-relative project identities, never machine paths.
        foreach($p in $report.projects) { $p.path=[IO.Path]::GetRelativePath($root,$p.path).Replace('\','/') }
        $reports+=@{Kind=$kind;Report=$report}
    }
    $assetsPath=Join-Path (Split-Path $project -Parent) 'obj/project.assets.json'
    if(!(Test-Path -LiteralPath $assetsPath)) { throw "Restore $project before reviewing." }
    $assets=Get-Content -LiteralPath $assetsPath -Raw | ConvertFrom-Json -AsHashtable
    foreach($p in $assets.libraries.GetEnumerator()) {
        if($p.Value.type -eq 'package') { $pairs[$p.Key]=$true }
    }
    foreach($f in $assets.project.frameworks.Values) {
        foreach($d in $f.downloadDependencies) { $pairs[$d.name+'/'+($d.version.Trim('[',']').Split(',')[0].Trim())]=$true }
    }
}
# The self-contained application uses this explicitly selected runtime on both
# supported RIDs. NuGet's ordinary package list omits SDK download dependencies.
foreach($rid in @('win-x64','win-arm64')) {
    if(Test-Path (Join-Path $root ".packages/microsoft.netcore.app.runtime.$rid/10.0.13")) {
        $pairs["Microsoft.NETCore.App.Runtime.$rid/10.0.13"]=$true
    }
}
$rows=@(foreach($pair in ($pairs.Keys | Sort-Object)) {
    $id,$version=$pair.Split('/')
    $uri='https://api.nuget.org/v3/registration5-gz-semver2/'+$id.ToLowerInvariant()+'/'+$version.ToLowerInvariant()+'.json'
    $record=Invoke-RestMethod -Uri $uri -TimeoutSec 30
    $meta=if($record.catalogEntry -is [string]) { Invoke-RestMethod $record.catalogEntry -TimeoutSec 30 } else { $record.catalogEntry }
    $index=Invoke-RestMethod ('https://api.nuget.org/v3-flatcontainer/'+$id.ToLowerInvariant()+'/index.json') -TimeoutSec 30
    $stable=@($index.versions | Where-Object { $_ -notmatch '-' })
    $latest=$null
    for($i=$stable.Count-1;$i -ge 0;$i--) {
        $v=$stable[$i]
        $entry=Invoke-RestMethod ('https://api.nuget.org/v3/registration5-gz-semver2/'+$id.ToLowerInvariant()+'/'+$v.ToLowerInvariant()+'.json') -TimeoutSec 30
        $candidate=if($entry.catalogEntry -is [string]) { Invoke-RestMethod $entry.catalogEntry -TimeoutSec 30 } else { $entry.catalogEntry }
        if($candidate.listed) { $latest=$v; break }
    }
    if(!$meta.listed -or $meta.deprecation -or $meta.vulnerabilities -or $version.Contains('-')) { throw "Review required: unlisted/deprecated/vulnerable/prerelease $pair." }
    $dir=Join-Path $root ('.packages/'+$id.ToLowerInvariant()+'/'+$version.ToLowerInvariant())
    $licenses=@(Get-ChildItem -LiteralPath $dir -File -Recurse | Where-Object Name -Match '^(LICENSE|LICENCE|NOTICE|THIRD.PARTY.NOTICES|sdk_license)(\.|$)' | ForEach-Object {
        @{Path=[IO.Path]::GetRelativePath($dir,$_.FullName).Replace('\','/');SHA256=(Get-FileHash -LiteralPath $_.FullName).Hash}
    })
    [ordered]@{Id=$id;Version=$version;LatestListedStable=$latest;Listed=$meta.listed;LicenseExpression=$meta.licenseExpression;LicenseUrl=$meta.licenseUrl;Upstream=$meta.projectUrl;Registry=$uri;LicenseMaterials=$licenses}
})
[ordered]@{ReviewedOn=(Get-Date).ToString('yyyy-MM-dd');Scope='All project restore graphs, SDK download dependencies and the selected self-contained .NET runtime for both installed RIDs.';Packages=$rows;Reports=$reports} |
    ConvertTo-Json -Depth 30 | Set-Content -LiteralPath $OutputPath -Encoding utf8NoBOM
Write-Host "Dependency evidence written: $OutputPath ($($rows.Count) package/version pairs). Review redistribution conditions separately."
