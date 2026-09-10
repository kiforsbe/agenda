param([string]$CertificatePath='')
$ErrorActionPreference='Stop'
$repoRoot=Split-Path $PSScriptRoot -Parent
if([string]::IsNullOrWhiteSpace($CertificatePath)) { $CertificatePath=Join-Path $repoRoot 'artifacts/signing/Agenda-Development.cer' }
$certificate=Resolve-Path -LiteralPath $CertificatePath -ErrorAction Stop
Import-Certificate -FilePath $certificate.Path -CertStoreLocation 'Cert:\CurrentUser\TrustedPeople' | Out-Null
Write-Output "Trusted development certificate: $($certificate.Path)"
