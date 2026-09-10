param([Parameter(Mandatory=$true)][string]$Tag)
$ErrorActionPreference='Stop'
$repoRoot=Split-Path $PSScriptRoot -Parent
Push-Location $repoRoot
try {
    $propsText=Get-Content -LiteralPath (Join-Path $repoRoot 'Directory.Build.props') -Raw
    $versionMatch=[regex]::Match($propsText,'<AgendaVersion>\s*([^<\s]+)\s*</AgendaVersion>')
    if(-not $versionMatch.Success) { throw 'Directory.Build.props does not define AgendaVersion.' }
    $version=$versionMatch.Groups[1].Value
    if($version -notmatch '^\d+\.\d+\.\d+$') { throw "AgendaVersion '$version' must use major.minor.patch format." }
    $expectedTag="v$version"
    if($Tag -ne $expectedTag) { throw "Tag '$Tag' does not match AgendaVersion '$version'. Expected '$expectedTag'." }
    $changelog=Get-Content -LiteralPath (Join-Path $repoRoot 'CHANGELOG.md') -Raw
    $headingPattern='(?m)^## \['+[regex]::Escape($version)+'\](?:\s|$)'
    if($changelog -notmatch $headingPattern) { throw "CHANGELOG.md has no '## [$version]' release heading." }
    Write-Output "Release metadata is ready for $expectedTag (AgendaVersion $version)."
} finally { Pop-Location }
