#requires -Version 7.4
function New-AgendaMsix([string]$Folder,[string]$Path,[string]$Version,[string]$Architecture,[string]$Publisher) {
    $root=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
    [xml]$manifest=Get-Content -LiteralPath (Join-Path $root 'deployment/AppxManifest.xml') -Raw
    $manifest.Package.Identity.ProcessorArchitecture=$Architecture.ToLowerInvariant()
    $manifest.Package.Identity.Publisher=$Publisher
    $manifest.Package.Identity.Version="$Version.0"
    $manifest.Save((Join-Path $Folder 'AppxManifest.xml'))
    Invoke-AgendaTool (Get-AgendaSdkTool makeappx.exe) @('pack','/d',$Folder,'/p',$Path)
    # Unpack the actual container and compare every staged file, including PRI/XBF.
    $inspection=Join-Path (Split-Path $Path -Parent) 'msix-inspection'
    Invoke-AgendaTool (Get-AgendaSdkTool makeappx.exe) @('unpack','/p',$Path,'/d',$inspection)
    foreach($file in Get-ChildItem -LiteralPath $Folder -File -Recurse) {
        $relative=[IO.Path]::GetRelativePath($Folder,$file.FullName)
        $unpacked=Join-Path $inspection $relative
        if(!(Test-Path -LiteralPath $unpacked) -or (Get-FileHash -LiteralPath $unpacked).Hash -ne (Get-FileHash -LiteralPath $file.FullName).Hash) { throw "MSIX payload mismatch: $relative" }
    }
}
