#requires -Version 7.4
function Assert-MsiVersion([string]$Version) {
    if($Version -notmatch '^(0|[1-9][0-9]{0,4})\.(0|[1-9][0-9]{0,4})\.(0|[1-9][0-9]{0,4})$') { throw 'MSI version must be stable major.minor.patch.' }
    $v=[version]$Version
    if($v.Major -gt 255 -or $v.Minor -gt 255 -or $v.Build -gt 65534) { throw 'MSI version exceeds 255.255.65534.' }
}
# The permanent product GUID is the namespace. Scope includes architecture and
# normalized relative file path; renaming a file deliberately changes its component.
function Get-MsiGuid([guid]$Namespace,[string]$Scope) {
    $bytes=[Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($Namespace.ToString('D')+'/'+$Scope.ToLowerInvariant().Replace('\','/')))
    # RFC 4122 variant/version bits; identities are deterministic SHA-256-derived UUIDs.
    $bytes[7]=($bytes[7] -band 15) -bor 128
    $bytes[8]=($bytes[8] -band 63) -bor 128
    return ([guid]::new([byte[]]$bytes[0..15])).ToString('B').ToUpperInvariant()
}
function Get-MsiId([string]$Prefix,[guid]$Namespace,[string]$Scope) {
    return $Prefix+(Get-MsiGuid $Namespace $Scope).Replace('-','').Trim('{}')
}
