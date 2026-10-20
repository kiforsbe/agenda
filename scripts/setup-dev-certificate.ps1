#requires -Version 7.4

<#
.SYNOPSIS
Explicitly creates or selects an Agenda development signing certificate.
.DESCRIPTION
This command is separate from packaging. It exports only a public .cer and
never imports trust. New keys are non-exportable; select the returned Thumbprint
when invoking package.ps1. Existing matching valid certificates can be reused.
#>

param([string]$Publisher='CN=Agenda.Development')
$ErrorActionPreference='Stop'
$repoRoot=Split-Path $PSScriptRoot -Parent
$codeSigningOid='1.3.6.1.5.5.7.3.3'
$certificate=Get-ChildItem -Path 'Cert:\CurrentUser\My' |
    Where-Object {
        $_.Subject -eq $Publisher -and $_.HasPrivateKey -and $_.NotBefore -le (Get-Date) -and $_.NotAfter -gt (Get-Date) -and
        ($_.EnhancedKeyUsageList | Where-Object { $_.ObjectId -eq $codeSigningOid })
    } |
    Sort-Object NotAfter -Descending |
    Select-Object -First 1
if(-not $certificate) {
    $certificate=New-SelfSignedCertificate -Type Custom -Subject $Publisher -FriendlyName 'Agenda development package signing' -CertStoreLocation 'Cert:\CurrentUser\My' -KeyExportPolicy NonExportable -HashAlgorithm SHA256 -KeyUsage DigitalSignature -TextExtension @("2.5.29.37={text}$codeSigningOid") -NotAfter (Get-Date).AddYears(1)
}
$signingDirectory=Join-Path $repoRoot 'artifacts/signing'
New-Item -ItemType Directory -Path $signingDirectory -Force | Out-Null
$publicCertificate=Join-Path $signingDirectory 'Agenda-Development.cer'
Export-Certificate -Cert $certificate -FilePath $publicCertificate -Type CERT | Out-Null
Write-Host "Development certificate subject: $($certificate.Subject)"
Write-Host "Development certificate thumbprint: $($certificate.Thumbprint)"
Write-Host "Public certificate: $publicCertificate"
Write-Host 'Trust was not installed. Select this certificate explicitly when signing a development MSIX.'
[pscustomobject]@{Thumbprint=$certificate.Thumbprint;Publisher=$certificate.Subject;PublicCertificate=$publicCertificate}
