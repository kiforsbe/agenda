#requires -Version 7.4

<#
.SYNOPSIS
Publishes and inspects Agenda installers; MSI is the default, MSIX is opt-in.
.DESCRIPTION
Both formats are unsigned unless an existing certificate is explicitly selected.
Packaging never creates certificates or imports trust. Each build has isolated
output and a release-artifacts.json describing the final bytes and source state.
Requires the initialized external/winbuildtools submodule (release v0.3.0).
Packaging, signing, SDK discovery, diagnostics and final byte/signature metadata
use that checkout. Agenda supplies publishing, product identity and release fields.
.PARAMETER Format
Msi by default. Select Msix explicitly for a Windows app package.
.PARAMETER CertificateThumbprint
Explicit code-signing certificate in CurrentUser/My, as in ProcSight tooling.
.PARAMETER CertificatePath
Alternative explicit PFX input; mutually exclusive with CertificateThumbprint.
.EXAMPLE
./scripts/package.ps1
.EXAMPLE
./scripts/package.ps1 -Format Msix
.EXAMPLE
./scripts/package.ps1 -Format Msix -CertificateThumbprint ABCDEF0123456789ABCDEF0123456789ABCDEF01
#>

[CmdletBinding()]
param(
    [ValidateSet('x64','ARM64')][string]$Architecture='x64',
    [ValidateSet('Msi','Msix')][string]$Format='Msi',
    [string]$Publisher='CN=Agenda.Development',
    [string]$CertificateThumbprint='',
    [string]$CertificatePath='',
    [string]$CertificatePassword='',
    [string]$TimestampUri=''
)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
. (Join-Path $PSScriptRoot 'packaging/Agenda.ps1')
$sign=[bool]($CertificateThumbprint -or $CertificatePath)
if($sign -or $CertificatePassword -or $TimestampUri) {
    Assert-WbtSigningOptions -CertificateThumbprint $CertificateThumbprint -CertificatePath $CertificatePath -CertificatePassword $CertificatePassword -TimestampUri $TimestampUri
}
if($CertificatePath) { $CertificatePath=Resolve-WbtPath $CertificatePath }
if($sign) {
    # Reject bad credentials/options before paying for restore and publish.
    $signingPublisher=if($Format -eq 'Msix') { $Publisher } else { '' }
    $certificate=Get-WbtSigningCertificate -CertificateThumbprint $CertificateThumbprint -CertificatePath $CertificatePath -CertificatePassword $CertificatePassword -Publisher $signingPublisher
    $certificate.Dispose()
    $null=Get-WbtSdkTool signtool.exe
}
$version=Get-AgendaVersion $root
$commit=(& git -C $root rev-parse HEAD).Trim()
if($LASTEXITCODE -ne 0) { throw 'Cannot determine source commit.' }
$sourceChanges=@(& git -C $root status --porcelain)
if($LASTEXITCODE -ne 0) { throw 'Cannot inspect source state.' }
$sourceDirty=[bool]$sourceChanges.Count
$buildRoot=Join-Path $root ("artifacts/packages/$Format/$version/$Architecture/"+[guid]::NewGuid().ToString('N'))
$previousHome=$env:DOTNET_CLI_HOME
Push-Location $root
try {
    $env:DOTNET_CLI_HOME=Join-Path $root '.dotnet'
    $null=New-Item -ItemType Directory -Path $buildRoot -Force
    $stage=Publish-AgendaPayload $Architecture $buildRoot
    $installer=New-AgendaInstaller -Root $root -Format $Format -PayloadDirectory $stage -Version $version -Architecture $Architecture -Publisher $Publisher -OutputDirectory (Join-Path $buildRoot 'installer')
    $artifact=$installer.Artifact
    if($Format -eq 'Msix') {
        $suffix=if($sign) { 'signed' } else { 'unsigned' }
        $artifact=Join-Path $buildRoot "Agenda-$version-$Architecture-$suffix.msix"
        # Retain the engine's unsigned artifact/inspection evidence; distribute
        # the Agenda-named copy and compute final hashes after optional signing.
        Copy-Item -LiteralPath $installer.Artifact -Destination $artifact
    }
    $result=Complete-AgendaArtifact -Artifact $artifact -Version $version -Commit $commit -Format $Format -Architecture $Architecture -SourceDirty:$sourceDirty -Sign:$sign -CertificateThumbprint $CertificateThumbprint -CertificatePath $CertificatePath -CertificatePassword $CertificatePassword -TimestampUri $TimestampUri
    if(!$sign -and $Format -eq 'Msix') {
        Write-Host 'Unsigned MSIX build artifact: sign with an explicitly selected publisher-matching certificate before installation.'
    } elseif(!$sign) { Write-Host 'Unsigned MSI created. Windows reputation or device policy may warn or block installation.' }
    Write-Host "Inspected $Format package: $artifact"
    Write-Host "Portable application: $stage/Agenda.App.exe"
    $result
} finally { $env:DOTNET_CLI_HOME=$previousHome; Pop-Location }
