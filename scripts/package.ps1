param([ValidateSet('x64','ARM64')][string]$Architecture='x64',[string]$Publisher='CN=Agenda.Development')
$ErrorActionPreference='Stop'
$repoRoot=Split-Path $PSScriptRoot -Parent
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
    Write-Output "Unsigned package: $package"
    Write-Output "Portable application: $stage/Agenda.App.exe"
    Write-Output 'Signing and certificate trust are deployment steps; this script does not change certificate stores.'
} finally { Pop-Location }
