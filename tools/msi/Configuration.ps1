#requires -Version 7.4
function Resolve-MsiPath([string]$Path) {
    # PowerShell Set-Location does not update .NET's process current directory.
    # The provider API also resolves destinations that do not exist yet.
    $provider=$null; $drive=$null
    $resolved=$ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Path,[ref]$provider,[ref]$drive)
    if($provider.Name -ne 'FileSystem') { throw 'MSI paths must use the filesystem provider.' }
    return [IO.Path]::GetFullPath($resolved)
}
function Assert-MsiRelativePath([string]$Path) {
    # Refuse device/stream syntax and ambiguous names instead of rewriting paths.
    if([string]::IsNullOrWhiteSpace($Path) -or $Path -match '[\x00-\x1f:"%<>|?*]' -or [IO.Path]::IsPathRooted($Path)) { throw "Invalid payload path: $Path" }
    foreach($part in $Path.Replace('\','/').Split('/')) {
        if(!$part -or $part -in @('.','..') -or $part -match '[. ]$|^(?i:CON|PRN|AUX|NUL|COM[0-9]|LPT[0-9])(?:\.|$)') { throw "Invalid path segment: $Path" }
    }
}
function Read-MsiConfiguration([string]$Path) {
    $json=[IO.File]::ReadAllText((Resolve-MsiPath $Path))
    # ConvertFrom-Json keeps the last duplicate property; inspect original syntax first.
    $document=[Text.Json.JsonDocument]::Parse($json)
    function Assert-UniqueJsonKeys($Node) {
        if($Node.ValueKind -eq [Text.Json.JsonValueKind]::Object) {
            $seen=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
            foreach($p in $Node.EnumerateObject()) { if(!$seen.Add($p.Name)) { throw "Duplicate JSON property: $($p.Name)" }; Assert-UniqueJsonKeys $p.Value }
        } elseif($Node.ValueKind -eq [Text.Json.JsonValueKind]::Array) {
            foreach($n in $Node.EnumerateArray()) { Assert-UniqueJsonKeys $n }
        }
    }
    try { Assert-UniqueJsonKeys $document.RootElement } finally { $document.Dispose() }
    if(!($json | Test-Json -SchemaFile (Join-Path $PSScriptRoot 'config.schema.json') -ErrorAction Stop)) { throw 'Invalid MSI configuration.' }
    $config=$json | ConvertFrom-Json -AsHashtable
    Assert-MsiRelativePath $config.installation.directoryName
    if($config.installation.directoryName -match '[/\\]') { throw 'Installation directoryName must be one segment.' }
    foreach($path in @($config.application.executable)+@($config.application.icon)+@($config.payload.requiredFiles)+@($config.payload.allowedFiles)) {
        if($path) { Assert-MsiRelativePath $path }
    }
    if([IO.Path]::GetExtension($config.application.executable) -ine '.exe') { throw 'Application executable must be an .exe.' }
    $names=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach($shortcut in $config.shortcuts) {
        Assert-MsiRelativePath $shortcut.name
        if($shortcut.name -match '[/\\]' -or $shortcut.name.Length -gt 64 -or !$names.Add($shortcut.name)) { throw 'Invalid or duplicate shortcut name.' }
    }
    foreach($value in @($config.product.name,$config.product.manufacturer)) {
        if($value.Length -gt 64 -or $value -match '[\x00-\x1f]') { throw 'Product display fields must contain 1..64 printable characters.' }
    }
    return $config
}
