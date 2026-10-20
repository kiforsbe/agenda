#requires -Version 7.4
# Signing consumes an explicitly selected key. Certificate creation and trust are
# separate commands; building either installer format must never create a key.
function Assert-AgendaSigningOptions {
    param([string]$CertificateThumbprint,[string]$CertificatePath,[string]$CertificatePassword,[string]$TimestampUri)
    if($CertificateThumbprint -and $CertificatePath) { throw 'Select CertificateThumbprint or CertificatePath, not both.' }
    if($CertificateThumbprint -and $CertificateThumbprint -notmatch '^[a-fA-F0-9]{40}$') { throw 'CertificateThumbprint must identify an explicit certificate in CurrentUser/My.' }
    if($CertificatePassword -and !$CertificatePath) { throw 'CertificatePassword requires CertificatePath.' }
    if($TimestampUri) {
        if(!$CertificateThumbprint -and !$CertificatePath) { throw 'TimestampUri requires an explicitly selected signing certificate.' }
        $uri=$null
        if(![uri]::TryCreate($TimestampUri,[UriKind]::Absolute,[ref]$uri) -or $uri.Scheme -ne 'https' -or $uri.UserInfo -or $uri.Fragment) { throw 'TimestampUri must be an absolute HTTPS URL.' }
    }
}
function Assert-AgendaSigningMetadata($Certificate,[string]$Publisher) {
    if(!$Certificate.HasPrivateKey -or $Certificate.NotBefore -gt (Get-Date) -or $Certificate.NotAfter -le (Get-Date) -or '1.3.6.1.5.5.7.3.3' -notin $Certificate.UsageOids) {
        throw 'Select an unexpired code-signing certificate with its private key.'
    }
    if($Publisher -and $Certificate.Subject -cne $Publisher) { throw 'MSIX publisher and signing certificate subject must match.' }
}
function Get-AgendaSigningCertificate {
    param([string]$CertificateThumbprint,[string]$CertificatePath,[string]$CertificatePassword,[string]$Publisher)
    Assert-AgendaSigningOptions -CertificateThumbprint $CertificateThumbprint -CertificatePath $CertificatePath -CertificatePassword $CertificatePassword
    if(!$CertificateThumbprint -and !$CertificatePath) { throw 'Signing requires CertificateThumbprint or CertificatePath.' }
    $certificate=$null
    try {
        if($CertificateThumbprint) {
            $certificate=Get-Item -LiteralPath "Cert:\CurrentUser\My\$CertificateThumbprint" -ErrorAction Stop
        } else {
            $resolved=(Resolve-Path -LiteralPath $CertificatePath -ErrorAction Stop).Path
            # Ephemeral import avoids persisting a supplied PFX key in a store.
            $certificate=[Security.Cryptography.X509Certificates.X509Certificate2]::new($resolved,$CertificatePassword,[Security.Cryptography.X509Certificates.X509KeyStorageFlags]::EphemeralKeySet)
        }
        $oids=@($certificate.Extensions | Where-Object { $_ -is [Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension] } | ForEach-Object { $_.EnhancedKeyUsages } | ForEach-Object Value)
        Assert-AgendaSigningMetadata ([pscustomobject]@{Subject=$certificate.Subject;HasPrivateKey=$certificate.HasPrivateKey;NotBefore=$certificate.NotBefore;NotAfter=$certificate.NotAfter;UsageOids=$oids}) $Publisher
        return $certificate
    } catch {
        if($certificate) { $certificate.Dispose() }
        throw
    }
}
function Invoke-AgendaSignature {
    param([string]$Artifact,[string]$Publisher,[string]$CertificateThumbprint,[string]$CertificatePath,[string]$CertificatePassword,[string]$TimestampUri)
    Assert-AgendaSigningOptions -CertificateThumbprint $CertificateThumbprint -CertificatePath $CertificatePath -CertificatePassword $CertificatePassword -TimestampUri $TimestampUri
    $certificate=Get-AgendaSigningCertificate -CertificateThumbprint $CertificateThumbprint -CertificatePath $CertificatePath -CertificatePassword $CertificatePassword -Publisher $Publisher
    try {
        $signTool=Get-AgendaSdkTool signtool.exe
        if($CertificateThumbprint) {
            $arguments=@('sign','/fd','SHA256','/sha1',$certificate.Thumbprint,'/s','My')
        } else {
            $arguments=@('sign','/fd','SHA256','/f',(Resolve-Path -LiteralPath $CertificatePath).Path)
            if($CertificatePassword) { $arguments+=@('/p',$CertificatePassword) }
        }
        if($TimestampUri) { $arguments+=@('/tr',$TimestampUri,'/td','SHA256') }
        # Do not log arguments: a PFX password is sensitive.
        & $signTool @arguments $Artifact | Out-Host
        if($LASTEXITCODE -ne 0) { throw 'Package signing failed.' }
        $signature=Get-AuthenticodeSignature -LiteralPath $Artifact
        if($signature.SignerCertificate.Thumbprint -ne $certificate.Thumbprint -or $signature.Status -in @('HashMismatch','NotSigned')) { throw 'Signed package does not match the selected certificate or its contents.' }
    } finally { $certificate.Dispose() }
}
