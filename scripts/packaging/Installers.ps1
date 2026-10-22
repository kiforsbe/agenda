#requires -Version 7.4
# Agenda owns product metadata and publishing. The pinned external tools own
# installer authoring and inspection; never download or update them in a build.
function Get-AgendaBuildToolsRoot([string]$Root) {
    $tools=Join-Path $Root 'external/winbuildtools'
    foreach($relative in @('msi/package.ps1','msix/package.ps1','verify.ps1','VERSION')) {
        if(!(Test-Path -LiteralPath (Join-Path $tools $relative) -PathType Leaf)) {
            throw 'winbuildtools checkout is missing or incomplete. Run: git submodule update --init --recursive'
        }
    }
    return $tools
}
function New-AgendaInstaller {
    param([string]$Root,[ValidateSet('Msi','Msix')][string]$Format,[string]$PayloadDirectory,[string]$Version,[ValidateSet('x64','ARM64')][string]$Architecture,[string]$Publisher='CN=Agenda.Development',[string]$OutputDirectory)
    $tools=Get-AgendaBuildToolsRoot $Root
    if($Format -eq 'Msi') {
        return & (Join-Path $tools 'msi/package.ps1') -Config (Join-Path $Root 'deployment/msi.json') -PayloadDirectory $PayloadDirectory -Version $Version -Architecture $Architecture -OutputDirectory $OutputDirectory
    }
    # Supply the normal MSIX XML, with Agenda's version/architecture/publisher.
    # Keep it beside the new output tree so portable publish files are unchanged
    # and the external engine receives no stale container manifest/signature.
    $manifestPath="$OutputDirectory.AppxManifest.xml"
    if(Test-Path -LiteralPath $manifestPath) { throw 'Generated MSIX manifest already exists; use a fresh build directory.' }
    [xml]$manifest=Get-Content -LiteralPath (Join-Path $Root 'deployment/AppxManifest.xml') -Raw
    $manifest.Package.Identity.ProcessorArchitecture=$Architecture.ToLowerInvariant()
    $manifest.Package.Identity.Publisher=$Publisher
    $manifest.Package.Identity.Version="$Version.0"
    $manifest.Save($manifestPath)
    & (Join-Path $tools 'msix/package.ps1') -PayloadDirectory $PayloadDirectory -Manifest $manifestPath -OutputDirectory $OutputDirectory
}
