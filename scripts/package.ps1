param(
    [ValidateSet('x64','ARM64')][string]$Architecture='x64',
    [string]$Publisher='CN=Agenda.Development',
    [string]$CertificatePath='',
    [string]$CertificatePassword=''
)
$ErrorActionPreference='Stop'
$repoRoot=Split-Path $PSScriptRoot -Parent
$temporaryPfx=$null
$temporaryCertificateThumbprint=$null
Push-Location $repoRoot
try {
    $env:DOTNET_CLI_HOME=Join-Path $repoRoot '.dotnet'
    $runtimeId='win-'+$Architecture.ToLowerInvariant()
    # A new staging folder avoids stale files and never removes a previous build.
    $stage=Join-Path $repoRoot ('artifacts/package-'+$Architecture+'-'+[Guid]::NewGuid().ToString('N'))
    dotnet restore src/Agenda.App --configfile NuGet.Config -p:Platform=$Architecture -r $runtimeId -p:SelfContained=true
    if ($LASTEXITCODE -ne 0) { throw 'Dependency restore failed.' }
    dotnet publish src/Agenda.App --no-restore -c Release -p:Platform=$Architecture -r $runtimeId --self-contained true -o $stage
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
    foreach($resource in @('Agenda.App.pri','App.xbf','MainWindow.xbf')) {
        if(-not(Test-Path -LiteralPath (Join-Path $stage $resource))) { throw "Publish omitted required XAML resource: $resource" }
    }
    & (Join-Path $PSScriptRoot 'create-assets.ps1')
    # The WinUI publish target can already create an Assets directory for tray
    # icons. Merge the deployment assets into it instead of copying a directory
    # over an existing directory, which makes packaging repeatable.
    $assetDestination=Join-Path $stage 'Assets'
    New-Item -ItemType Directory -Path $assetDestination -Force | Out-Null
    Copy-Item -Path (Join-Path $repoRoot 'deployment/Assets/*') -Destination $assetDestination -Recurse -Force
    $propsText=Get-Content -LiteralPath (Join-Path $repoRoot 'Directory.Build.props') -Raw
    $versionMatch=[regex]::Match($propsText,'<AgendaVersion>\s*([^<\s]+)\s*</AgendaVersion>')
    if(-not $versionMatch.Success -or $versionMatch.Groups[1].Value -notmatch '^\d+\.\d+\.\d+$') { throw 'Directory.Build.props must define AgendaVersion as major.minor.patch.' }
    $packageVersion=$versionMatch.Groups[1].Value+'.0'
    [xml]$manifest=Get-Content -LiteralPath (Join-Path $repoRoot 'deployment/AppxManifest.xml') -Raw
    $manifest.Package.Identity.ProcessorArchitecture=$Architecture.ToLowerInvariant()
    $manifest.Package.Identity.Publisher=$Publisher
    $manifest.Package.Identity.Version=$packageVersion
    $manifest.Save((Join-Path $stage 'AppxManifest.xml'))
    $sdkBin=Join-Path ${env:ProgramFiles(x86)} 'Windows Kits/10/bin'
    $makeappx=Get-ChildItem -LiteralPath $sdkBin -Directory | Where-Object Name -Match '^10\.' | Sort-Object { [Version]$_.Name } -Descending | ForEach-Object { Join-Path $_.FullName 'x64/makeappx.exe' } | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    if (-not $makeappx) { throw 'Windows SDK makeappx.exe was not found.' }
    $package=Join-Path $repoRoot "artifacts/Agenda-$Architecture.msix"
    & $makeappx pack /d $stage /p $package /o | Out-File -LiteralPath (Join-Path $repoRoot "artifacts/makeappx-$Architecture.log")
    if ($LASTEXITCODE -ne 0) { throw 'MSIX packaging failed.' }
    $signtool=Get-ChildItem -LiteralPath $sdkBin -Directory | Where-Object Name -Match '^10\.' | Sort-Object { [Version]$_.Name } -Descending | ForEach-Object { Join-Path $_.FullName 'x64/signtool.exe' } | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    if (-not $signtool) { throw 'Windows SDK signtool.exe was not found.' }

    $publicCertificate=$null
    if([string]::IsNullOrWhiteSpace($CertificatePath)) {
        $signingDirectory=Join-Path $repoRoot 'artifacts/signing'
        New-Item -ItemType Directory -Path $signingDirectory -Force | Out-Null
        $publicCertificate=Join-Path $signingDirectory 'Agenda-Development.cer'
        $temporaryPfx=[IO.Path]::GetTempFileName()
        $certificate=New-SelfSignedCertificate -Type Custom -Subject $Publisher -FriendlyName 'Agenda development package signing' -CertStoreLocation 'Cert:\CurrentUser\My' -KeyUsage DigitalSignature -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3') -NotAfter (Get-Date).AddYears(5)
        $temporaryCertificateThumbprint=$certificate.Thumbprint
        $temporaryPassword=[Guid]::NewGuid().ToString('N')
        $securePassword=ConvertTo-SecureString $temporaryPassword -AsPlainText -Force
        Export-PfxCertificate -Cert $certificate -FilePath $temporaryPfx -Password $securePassword | Out-Null
        Export-Certificate -Cert $certificate -FilePath $publicCertificate -Type CERT | Out-Null
        $signArguments=@('sign','/fd','SHA256','/f',$temporaryPfx,'/p',$temporaryPassword,'/a',$package)
    } else {
        $resolvedCertificate=Resolve-Path -LiteralPath $CertificatePath -ErrorAction Stop
        $signArguments=@('sign','/fd','SHA256','/f',$resolvedCertificate.Path)
        if(-not [string]::IsNullOrWhiteSpace($CertificatePassword)) { $signArguments+=@('/p',$CertificatePassword) }
        $signArguments+=$package
    }
    & $signtool @signArguments
    if ($LASTEXITCODE -ne 0) { throw 'MSIX signing failed.' }
    Write-Output "Signed package: $package"
    if($publicCertificate) { Write-Output "Development certificate: $publicCertificate" }
    Write-Output "Portable application: $stage/Agenda.App.exe"
    Write-Output 'Trust the development .cer in the current user TrustedPeople store before installing a locally signed package.'
} finally {
    if($temporaryPfx) { Remove-Item -LiteralPath $temporaryPfx -Force -ErrorAction SilentlyContinue }
    if($temporaryCertificateThumbprint) { Remove-Item -LiteralPath "Cert:\CurrentUser\My\$temporaryCertificateThumbprint" -Force -ErrorAction SilentlyContinue }
    Pop-Location
}
