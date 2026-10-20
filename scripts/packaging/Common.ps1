#requires -Version 7.4
. (Join-Path $PSScriptRoot '../dependencies/Common.ps1')
function Get-AgendaVersion([string]$Root) {
    [xml]$props=Get-Content -LiteralPath (Join-Path $Root 'Directory.Build.props') -Raw
    $version=[string]@($props.Project.PropertyGroup.AgendaVersion | Where-Object {$_})[0]
    if($version -notmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$') { throw 'AgendaVersion must be major.minor.patch.' }
    return $version
}
function Invoke-AgendaTool([string]$Tool,[string[]]$Arguments) {
    # Like ProcSight, retain full diagnostics but keep routine package output
    # concise. Never call this logger with signing arguments/PFX passwords.
    $messages=@(& $Tool @Arguments 2>&1)
    $exitCode=$LASTEXITCODE
    if($script:AgendaToolLog) { $messages | Out-String | Add-Content -LiteralPath $script:AgendaToolLog }
    if($exitCode -ne 0) {
        $messages | Select-Object -Last 12 | Out-Host
        throw "$([IO.Path]::GetFileName($Tool)) failed (exit $exitCode). See tool-output.log."
    }
    Write-Host "$([IO.Path]::GetFileName($Tool)): succeeded"
}
function Get-AgendaSdkTool([ValidateSet('makeappx.exe','signtool.exe')][string]$Name) {
    $sdk=Join-Path ${env:ProgramFiles(x86)} 'Windows Kits/10/bin'
    foreach($dir in Get-ChildItem -LiteralPath $sdk -Directory | Where-Object Name -Match '^10\.' | Sort-Object { [version]$_.Name } -Descending) {
        $path=Join-Path $dir.FullName "x64/$Name"
        if(Test-Path -LiteralPath $path) { return $path }
    }
    throw "Windows SDK $Name not found."
}
function Assert-AgendaPayload([string]$Folder) {
    foreach($name in @('Agenda.App.exe','Agenda.App.dll','Agenda.App.pri','App.xbf','MainWindow.xbf','Microsoft.WindowsAppRuntime.Insights.Resource.dll')) {
        if(!(Test-Path -LiteralPath (Join-Path $Folder $name) -PathType Leaf)) { throw "Published Agenda is missing $name" }
    }
}
function Export-AgendaLicenseMaterials([string]$AssetsPath,[string]$Destination) {
    $assets=Get-Content -LiteralPath $AssetsPath -Raw | ConvertFrom-Json -AsHashtable
    $licenses=Join-Path $Destination 'Licenses'
    $null=New-Item -ItemType Directory -Path $licenses -Force
    foreach($pair in $assets.libraries.GetEnumerator()) {
        if($pair.Value.type -ne 'package') { continue }
        $found=$false
        foreach($packageRoot in $assets.packageFolders.Keys) {
            $source=Join-Path $packageRoot $pair.Value.path
            if(!(Test-Path -LiteralPath $source)) { continue }
            [xml]$nuspec=Get-Content -LiteralPath (Get-ChildItem -LiteralPath $source -Filter '*.nuspec' | Select-Object -First 1).FullName -Raw
            $target=Join-Path $licenses $pair.Key.Replace('/','-')
            $null=New-Item -ItemType Directory -Path $target -Force
            $notices=@(Get-ChildItem -LiteralPath $source -File -Recurse | Where-Object { $_.Name -match '^(LICENSE|LICENCE|NOTICE|THIRD.PARTY.NOTICES|sdk_license)(\.|$)' })
            $license=$nuspec.package.metadata.license
            if($license.type -eq 'file') {
                $licenseFile=Join-Path $source $license.InnerText
                if(!(Test-Path -LiteralPath $licenseFile)) { throw "Missing declared license file for $($pair.Key)." }
                $notices+=Get-Item -LiteralPath $licenseFile
            }
            # Some expression/URL-licensed packages omit the actual text. Use
            # version-specific upstream materials reviewed and vendored in Agenda.
            $fallback=Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) ('deployment/licenses/'+$pair.Key.Replace('/','-'))
            if(Test-Path -LiteralPath $fallback) {
                Get-ChildItem -LiteralPath $fallback -File | Copy-Item -Destination $target
                $found=$true
            }
            if(!$notices.Count -and $found) { break }
            if(!$notices.Count) { throw "No redistribution license material found for $($pair.Key); review package terms." }
            foreach($notice in $notices | Sort-Object FullName -Unique) {
                $relative=[IO.Path]::GetRelativePath($source,$notice.FullName)
                if(![IO.Path]::GetExtension($relative)) { $relative+='.txt' }
                $dest=Join-Path $target $relative
                $null=New-Item -ItemType Directory -Path (Split-Path $dest -Parent) -Force
                Copy-Item -LiteralPath $notice.FullName -Destination $dest
            }
            $found=$true; break
        }
        if(!$found) { throw "Cannot locate resolved package $($pair.Key)." }
    }
}
# Application-specific work stays here: the generic MSI engine knows nothing
# about .NET restore/publish, WinUI resources, Git, signing or dependency licenses.
function Publish-AgendaPayload([string]$Architecture,[string]$BuildRoot) {
    $root=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
    $project=Join-Path $root 'src/Agenda.App/Agenda.App.csproj'
    $stage=Join-Path $BuildRoot 'publish'
    $common=@("-p:AgendaPackagingRoot=$BuildRoot/","-p:Platform=$Architecture",('-p:RuntimeIdentifier=win-'+$Architecture.ToLowerInvariant()),'-p:SelfContained=true')
    Invoke-AgendaTool dotnet (@('restore',$project,'--locked-mode','--configfile',(Join-Path $root 'NuGet.Config'))+(Get-AgendaAuditArguments)+$common)
    # The isolated restore above is the advisory gate for this exact RID graph.
    # A second package-list query would evaluate normal development obj paths and,
    # on .NET 10, restore them implicitly. Never audit a different graph here.
    Invoke-AgendaTool dotnet (@('publish',$project,'--no-restore','-c','Release','-o',$stage,'-p:DebugType=None','-p:DebugSymbols=false')+$common)
    Assert-AgendaPayload $stage
    & (Join-Path $root 'scripts/create-assets.ps1') | Out-Host
    $null=New-Item -ItemType Directory -Path (Join-Path $stage 'Assets') -Force
    Copy-Item -Path (Join-Path $root 'deployment/Assets/*') -Destination (Join-Path $stage 'Assets') -Recurse -Force
    Export-AgendaLicenseMaterials (Join-Path $BuildRoot 'obj/Agenda.App/project.assets.json') $stage
    $runtimeInfo=& dotnet msbuild $project -t:ResolveFrameworkReferences -getItem:ResolvedRuntimePack -nologo @common
    if($LASTEXITCODE -ne 0) { throw 'Cannot inspect runtime-pack notices.' }
    foreach($runtime in ($runtimeInfo | Out-String | ConvertFrom-Json).Items.ResolvedRuntimePack) {
        if(!$runtime.PackageDirectory) { continue }
        if($runtime.Identity -eq 'Microsoft.Windows.SDK.NET.Ref') {
            $target=Join-Path $stage ("Licenses/"+$runtime.Identity+'-'+$runtime.NuGetPackageVersion)
            $null=New-Item -ItemType Directory -Path $target -Force
            Copy-Item -LiteralPath (Join-Path $root 'deployment/licenses/Microsoft.Windows.SDK.BuildTools-10.0.28000.2705/LICENSE.rtf') -Destination (Join-Path $target 'LICENSE.rtf')
            continue
        }
        $notices=@(Get-ChildItem -LiteralPath $runtime.PackageDirectory -File | Where-Object Name -Match '^(LICENSE|THIRD.PARTY.NOTICES|NOTICE)(\.|$)')
        if(!$notices.Count) { throw "Runtime pack $($runtime.Identity) lacks license materials." }
        $target=Join-Path $stage ("Licenses/"+$runtime.Identity+'-'+$runtime.NuGetPackageVersion)
        $null=New-Item -ItemType Directory -Path $target -Force
        foreach($notice in $notices) {
            $name=$notice.Name
            if(![IO.Path]::GetExtension($name)) { $name+='.txt' }
            Copy-Item -LiteralPath $notice.FullName -Destination (Join-Path $target $name)
        }
    }
    Copy-Item -LiteralPath (Join-Path $root 'LICENSE') -Destination (Join-Path $stage 'Agenda-LICENSE.txt')
    Copy-Item -LiteralPath (Join-Path $root 'THIRD_PARTY_NOTICES.md') -Destination (Join-Path $stage 'Agenda-THIRD-PARTY-NOTICES.md')
    return $stage
}
function Complete-AgendaArtifact {
    param([string]$Artifact,[string]$Version,[string]$Commit,[string]$Format,[string]$Architecture,[switch]$SourceDirty,[switch]$Sign,[string]$Publisher,[string]$CertificateThumbprint,[string]$CertificatePath,[string]$CertificatePassword,[string]$TimestampUri)
    $signingPublisher=if($Format -eq 'Msix') { $Publisher } else { '' }
    if($Sign) { Invoke-AgendaSignature -Artifact $Artifact -Publisher $signingPublisher -CertificateThumbprint $CertificateThumbprint -CertificatePath $CertificatePath -CertificatePassword $CertificatePassword -TimestampUri $TimestampUri }
    $manifest=Join-Path (Split-Path $Artifact -Parent) 'release-artifacts.json'
    [pscustomobject]@{Version=$Version;Commit=$Commit;SourceDirty=[bool]$SourceDirty;Format=$Format;Architecture=$Architecture;Artifact=[IO.Path]::GetFileName($Artifact);SHA256=(Get-FileHash -LiteralPath $Artifact -Algorithm SHA256).Hash;Signed=[bool]$Sign} |
        ConvertTo-Json | Set-Content -LiteralPath $manifest -Encoding utf8NoBOM
    [pscustomobject]@{Artifact=$Artifact;Manifest=$manifest;Version=$Version;Commit=$Commit}
}
