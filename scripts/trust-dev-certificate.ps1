#Requires -RunAsAdministrator
param([string]$CertificatePath='')
$ErrorActionPreference='Stop'
$repoRoot=Split-Path $PSScriptRoot -Parent
if([string]::IsNullOrWhiteSpace($CertificatePath)) { $CertificatePath=Join-Path $repoRoot 'artifacts/signing/Agenda-Development.cer' }
$certificate=Resolve-Path -LiteralPath $CertificatePath -ErrorAction Stop
# MSIX deployment checks machine trust, even when installing for one user.
# Trust only the signing certificate; it does not need to become a root CA.
$trustedCertificate=Import-Certificate -FilePath $certificate.Path -CertStoreLocation 'Cert:\LocalMachine\TrustedPeople'
if(-not (Test-Path -LiteralPath "Cert:\LocalMachine\TrustedPeople\$($trustedCertificate.Thumbprint)")) {
    throw 'The development certificate was not added to Local Machine Trusted People.'
}
Write-Output "Trusted development certificate in Local Machine Trusted People: $($trustedCertificate.Thumbprint)"
