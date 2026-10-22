#requires -Version 7.4
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$helper=Join-Path $PSScriptRoot 'packaging/Installers.ps1'
if(Test-Path -LiteralPath $helper) { . $helper }
function Check([bool]$Value,[string]$Message) { if(!$Value) { throw $Message } }
function Rejected([scriptblock]$Action,[string]$Pattern) {
    $errorRecord=$null
    try { & $Action | Out-Null } catch { $errorRecord=$_ }
    Check ($null -ne $errorRecord -and $errorRecord.ToString() -match $Pattern) "Expected rejection matching $Pattern; got $errorRecord"
}
Check ([bool](Get-Command New-AgendaInstaller -ErrorAction SilentlyContinue)) 'Missing shared installer adapter.'
$fixture=Join-Path ([IO.Path]::GetTempPath()) ('AgendaInstallers-'+[guid]::NewGuid().ToString('N'))
$null=New-Item -ItemType Directory -Path "$fixture/consumer/scripts/packaging" -Force
try {
    # A missing checkout must fail before application restore/publish starts.
    Rejected { Get-AgendaBuildToolsRoot "$fixture/consumer" } 'git submodule update --init --recursive'
    Copy-Item -LiteralPath "$root/scripts/package.ps1" -Destination "$fixture/consumer/scripts/package.ps1"
    Copy-Item -LiteralPath $helper -Destination "$fixture/consumer/scripts/packaging/Installers.ps1"
    Rejected { & "$fixture/consumer/scripts/package.ps1" } 'git submodule update --init --recursive'

    $tools=Get-AgendaBuildToolsRoot $root
    Check ((Get-Content "$tools/VERSION" -Raw).Trim() -eq '0.1.0') 'Build tools version stays separate from Agenda version.'
    $commit=(& git -C $tools rev-parse HEAD).Trim()
    Check ($commit -eq '5ebe3c79ec35cfbcd015eb1de17848602375f883') 'Exact v0.1.0 tool checkout.'
    Check (!(Test-Path "$root/tools/msi") -and !(Test-Path "$root/scripts/packaging/Msix.ps1")) 'Duplicate installer implementations removed.'

    # Stub only the external command boundary to inspect product translation;
    # real native engine tests and application packages cover the actual tools.
    $null=New-Item -ItemType Directory -Path "$fixture/consumer/external/winbuildtools/msi" -Force
    $null=New-Item -ItemType Directory -Path "$fixture/consumer/external/winbuildtools/msix" -Force
    $null=New-Item -ItemType Directory -Path "$fixture/consumer/deployment" -Force
    $null=New-Item -ItemType Directory -Path "$fixture/payload" -Force
    Copy-Item -LiteralPath "$root/deployment/AppxManifest.xml" -Destination "$fixture/consumer/deployment/AppxManifest.xml"
    Set-Content "$fixture/consumer/external/winbuildtools/VERSION" '0.1.0'
    Set-Content "$fixture/consumer/external/winbuildtools/verify.ps1" '# Fixture entry point'
    Set-Content "$fixture/consumer/external/winbuildtools/msi/package.ps1" @'
param($Config,$PayloadDirectory,$Version,$Architecture,$OutputDirectory)
[pscustomobject]@{Artifact="$OutputDirectory/Agenda-$Version-$Architecture.msi";Config=$Config;Payload=$PayloadDirectory;Version=$Version;Architecture=$Architecture}
'@
    Set-Content "$fixture/consumer/external/winbuildtools/msix/package.ps1" @'
param($PayloadDirectory,$Manifest,$OutputDirectory)
$null=New-Item -ItemType Directory -Path $OutputDirectory
$artifact=Join-Path $OutputDirectory 'package-unsigned.msix'
[IO.File]::WriteAllText($artifact,'synthetic external artifact')
[pscustomobject]@{Artifact=$artifact;Manifest=$Manifest;Payload=$PayloadDirectory}
'@
    $msi=New-AgendaInstaller -Root "$fixture/consumer" -Format Msi -PayloadDirectory "$fixture/payload" -Version 7.8.9 -Architecture ARM64 -OutputDirectory "$fixture/msi output"
    Check ($msi.Version -eq '7.8.9' -and $msi.Architecture -eq 'ARM64' -and [IO.Path]::GetFullPath($msi.Config) -eq [IO.Path]::GetFullPath("$fixture/consumer/deployment/msi.json")) 'MSI receives caller product/version/architecture/configuration.'
    $msix=New-AgendaInstaller -Root "$fixture/consumer" -Format Msix -PayloadDirectory "$fixture/payload" -Version 7.8.9 -Architecture ARM64 -Publisher 'CN=Selected.Publisher' -OutputDirectory "$fixture/msix output"
    [xml]$manifest=Get-Content -LiteralPath $msix.Manifest -Raw
    Check ($manifest.Package.Identity.Version -eq '7.8.9.0' -and $manifest.Package.Identity.ProcessorArchitecture -eq 'arm64' -and $manifest.Package.Identity.Publisher -eq 'CN=Selected.Publisher') 'Agenda translates MSIX manifest metadata for external packer.'
    Check (!(Test-Path "$fixture/payload/AppxManifest.xml")) 'Product manifest never mutates portable publish directory.'
    Check (Test-Path -LiteralPath $msix.Artifact) 'Return actual external artifact for later Agenda signing/hashes.'
    Write-Host 'Shared installer adapter checks passed.'
} finally {
    $target=[IO.Path]::GetFullPath($fixture)
    if([IO.Path]::GetDirectoryName($target) -ne [IO.Path]::GetTempPath().TrimEnd('\') -or [IO.Path]::GetFileName($target) -notlike 'AgendaInstallers-*') { throw 'Unsafe installer test cleanup.' }
    Remove-Item -LiteralPath $target -Recurse -Force
}
