param([string]$CertificatePath='')
$ErrorActionPreference='Stop'
$repoRoot=Split-Path $PSScriptRoot -Parent
if([string]::IsNullOrWhiteSpace($CertificatePath)) { $CertificatePath=Join-Path $repoRoot 'artifacts/signing/Agenda-Development.cer' }
$certificate=Resolve-Path -LiteralPath $CertificatePath -ErrorAction Stop
foreach($store in @('Cert:\CurrentUser\Root','Cert:\CurrentUser\TrustedPeople')) {
    Import-Certificate -FilePath $certificate.Path -CertStoreLocation $store | Out-Null
}
Write-Output "Trusted development certificate in Root and TrustedPeople: $($certificate.Path)"
