param([string]$Publisher='CN=Agenda.Development')
$ErrorActionPreference='Stop'
$repoRoot=Split-Path $PSScriptRoot -Parent
$codeSigningOid='1.3.6.1.5.5.7.3.3'
$certificate=Get-ChildItem -Path 'Cert:\CurrentUser\My' |
    Where-Object {
        $_.Subject -eq $Publisher -and $_.HasPrivateKey -and $_.NotAfter -gt (Get-Date) -and
        ($_.EnhancedKeyUsageList | Where-Object { $_.ObjectId -eq $codeSigningOid })
    } |
    Sort-Object NotAfter -Descending |
    Select-Object -First 1
if(-not $certificate) {
    $certificate=New-SelfSignedCertificate -Type Custom -Subject $Publisher -FriendlyName 'Agenda development package signing' -CertStoreLocation 'Cert:\CurrentUser\My' -KeyUsage DigitalSignature -TextExtension @("2.5.29.37={text}$codeSigningOid") -NotAfter (Get-Date).AddYears(5)
}
$signingDirectory=Join-Path $repoRoot 'artifacts/signing'
New-Item -ItemType Directory -Path $signingDirectory -Force | Out-Null
$publicCertificate=Join-Path $signingDirectory 'Agenda-Development.cer'
Remove-Item -LiteralPath $publicCertificate -Force -ErrorAction SilentlyContinue
Export-Certificate -Cert $certificate -FilePath $publicCertificate -Type CERT | Out-Null
Write-Output "Development certificate subject: $($certificate.Subject)"
Write-Output "Development certificate thumbprint: $($certificate.Thumbprint)"
Write-Output "Public certificate: $publicCertificate"
