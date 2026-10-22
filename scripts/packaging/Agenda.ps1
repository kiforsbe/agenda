#requires -Version 7.4
# Agenda product adapter: generic packaging mechanisms are imported from the
# pinned checkout. Only publishing, resource/license policy and product metadata
# live here. No certificate creation, trust, signing, SDK or hashing code is copied.
function Get-AgendaBuildToolsRoot([string]$Root) {
    $tools=Join-Path $Root 'external/winbuildtools'
    foreach($relative in @('msi/package.ps1','msix/package.ps1','common/Tools.ps1','common/Artifacts.ps1','signing/Signing.ps1','verify.ps1','VERSION')) {
        if(!(Test-Path -LiteralPath (Join-Path $tools $relative) -PathType Leaf)) {
            throw 'winbuildtools checkout is missing or incomplete. Run: git submodule update --init --recursive'
        }
    }
    return $tools
}

function New-AgendaInstaller {
    param([string]$Root,[ValidateSet('Msi','Msix')][string]$Format,[string]$PayloadDirectory,[string]$Version,[ValidateSet('x64','ARM64')][string]$Architecture,[string]$Publisher='CN=Agenda.Development',[string]$OutputDirectory)
    $tools=Get-AgendaBuildToolsRoot $Root
    if($Format -eq 'Msi') {
        return & (Join-Path $tools 'msi/package.ps1') -Config (Join-Path $Root 'deployment/msi.json') -PayloadDirectory $PayloadDirectory -Version $Version -Architecture $Architecture -OutputDirectory $OutputDirectory
    }
    # Supply the normal MSIX XML, with Agenda's version/architecture/publisher.
    # Keep it beside the new output tree so portable publish files are unchanged
    # and the external engine receives no stale container manifest/signature.
    $manifestPath="$OutputDirectory.AppxManifest.xml"
    if(Test-Path -LiteralPath $manifestPath) { throw 'Generated MSIX manifest already exists; use a fresh build directory.' }
    [xml]$manifest=Get-Content -LiteralPath (Join-Path $Root 'deployment/AppxManifest.xml') -Raw
    $manifest.Package.Identity.ProcessorArchitecture=$Architecture.ToLowerInvariant()
    $manifest.Package.Identity.Publisher=$Publisher
    $manifest.Package.Identity.Version="$Version.0"
    $manifest.Save($manifestPath)
    & (Join-Path $tools 'msix/package.ps1') -PayloadDirectory $PayloadDirectory -Manifest $manifestPath -OutputDirectory $OutputDirectory
}
$agendaRoot=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$script:AgendaBuildToolsRoot=Get-AgendaBuildToolsRoot $agendaRoot
. (Join-Path $script:AgendaBuildToolsRoot 'common/Tools.ps1')
. (Join-Path $script:AgendaBuildToolsRoot 'common/Artifacts.ps1')
. (Join-Path $script:AgendaBuildToolsRoot 'signing/Signing.ps1')
. (Join-Path $PSScriptRoot '../dependencies/Common.ps1')
function Get-AgendaVersion([string]$Root) {
    [xml]$props=Get-Content -LiteralPath (Join-Path $Root 'Directory.Build.props') -Raw
    $version=[string]@($props.Project.PropertyGroup.AgendaVersion | Where-Object {$_})[0]
    if($version -notmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$') { throw 'AgendaVersion must be major.minor.patch.' }
    return $version
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

function Publish-AgendaPayload([string]$Architecture,[string]$BuildRoot) {
    $root=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
    $project=Join-Path $root 'src/Agenda.App/Agenda.App.csproj'
    $stage=Join-Path $BuildRoot 'publish'
    $log=Join-Path $BuildRoot 'tool-output.log'
    $common=@("-p:AgendaPackagingRoot=$BuildRoot/","-p:Platform=$Architecture",('-p:RuntimeIdentifier=win-'+$Architecture.ToLowerInvariant()),'-p:SelfContained=true')
    Invoke-WbtTool dotnet (@('restore',$project,'--locked-mode','--configfile',(Join-Path $root 'NuGet.Config'))+(Get-AgendaAuditArguments)+$common) -LogPath $log
    # The isolated restore above is the advisory gate for this exact RID graph.
    # A second package-list query would evaluate normal development obj paths and,
    # on .NET 10, restore them implicitly. Never audit a different graph here.
    Invoke-WbtTool dotnet (@('publish',$project,'--no-restore','-c','Release','-o',$stage,'-p:DebugType=None','-p:DebugSymbols=false')+$common) -LogPath $log
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
    param([string]$Artifact,[string]$Version,[string]$Commit,[string]$Format,[string]$Architecture,[switch]$SourceDirty,[switch]$Sign,[string]$CertificateThumbprint,[string]$CertificatePath,[string]$CertificatePassword,[string]$TimestampUri)
    $manifest=Join-Path (Split-Path $Artifact -Parent) 'release-artifacts.json'
    if(Test-Path -LiteralPath $manifest) { throw 'Release manifest already exists; use a fresh build directory.' }
    if($Sign) {
        # Shared signing reads the actual embedded MSIX publisher and validates
        # the final signature; no output manifest is written after a failure.
        $null=Invoke-WbtSignature -Artifact $Artifact -CertificateThumbprint $CertificateThumbprint -CertificatePath $CertificatePath -CertificatePassword $CertificatePassword -TimestampUri $TimestampUri
    }
    $result=Write-WbtArtifactManifest -Artifact $Artifact -OutputPath $manifest -Version $Version -SourceCommit $Commit -SourceDirty:$SourceDirty
    # Preserve the shared record verbatim except for Agenda's release fields.
    # Commit is the historical alias of SourceCommit; hashes/signature state are
    # measured only by winbuildtools, never derived from the requested Sign flag.
    $record=Get-Content -LiteralPath $result.Manifest -Raw | ConvertFrom-Json
    $record | Add-Member -NotePropertyMembers @{Commit=$Commit;Format=$Format;Architecture=$Architecture}
    $record | ConvertTo-Json | Set-Content -LiteralPath $result.Manifest -Encoding utf8NoBOM
    [pscustomobject]@{Artifact=$result.Artifact;Manifest=$result.Manifest;Version=$Version;Commit=$Commit}
}
