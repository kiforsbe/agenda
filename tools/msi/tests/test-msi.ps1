#requires -Version 7.4
$ErrorActionPreference='Stop'
$engine=Split-Path $PSScriptRoot -Parent
foreach($name in @('Configuration','Identity','Payload','Database','Authoring','Inspection')) {
 if(Test-Path "$engine/$name.ps1") { . "$engine/$name.ps1" }
}
if(!(Get-Command New-MsiPackage -ErrorAction SilentlyContinue)) { throw 'Missing native MSI authoring.' }
$script:checks=0
function Check([bool]$Value,[string]$Message) { if(!$Value) { throw $Message }; $script:checks++ }
function Rejected([scriptblock]$Action) { $failed=$false; try { & $Action | Out-Null } catch { $failed=$true }; Check $failed 'Expected MSI rejection.' }
$fixture=Join-Path ([IO.Path]::GetTempPath()) ('GenericMsi-'+[guid]::NewGuid().ToString('N'))
$null=New-Item -ItemType Directory -Path "$fixture/payload/Assets" -Force
try {
 $config=Read-MsiConfiguration "$engine/example.json"
 foreach($name in @('Example.exe','Assets/Example.ico')) { [IO.File]::WriteAllText("$fixture/payload/$name",'Synthetic fixture '+$name) }
 $prior=@{}
 foreach($arch in @('x64','ARM64')) {
  $path="$fixture/$arch.msi"
  New-MsiPackage $config "$fixture/payload" $path '0.1.0' $arch
  $info=Read-MsiPackage $path
  Check ($info.Properties.ProductName -ceq 'Example') 'Configured product name.'
  Check ($info.Template -ceq $(if($arch -eq 'x64'){'x64;1033'}else{'Arm64;1033'})) 'Native architecture.'
  Check ($info.CustomActionCount -eq 0 -and $info.RegistryCount -eq 0) 'No custom actions or authored registry.'
  Check ('Signature' -in $info.Tables) 'Required AppSearch table.'
  Check ($info.Files.Count -eq 2 -and $info.FeatureComponents.Count -eq 2) 'Payload feature membership.'
  Check ($info.Properties.ALLUSERS -eq '1') 'Machine install.'
  $installName=@($info.Directories | Where-Object Directory -eq 'INSTALLFOLDER')[0].DefaultDir
  Check (($installName -split '\|')[-1] -eq "Example ($($arch.ToLowerInvariant()))") 'Architecture directories cannot overwrite one another.'
  Check ($installName -match '^[A-Z0-9]{8}\|') 'Root directories carry valid short/long aliases.'
  Check ($info.Shortcuts[0].Name -match '^[A-Z0-9]{8}\|Example$') 'Shortcuts carry valid short/long aliases.'
  Check ($info.LaunchConditions -match '22000') 'Configured OS floor.'
  Check ($info.UpgradeCount -eq 2) 'Upgrade and downgrade rows.'
  $hash=(Get-FileHash $path).Hash
  Rejected { New-MsiPackage $config "$fixture/payload" $path '0.1.0' $arch }
  Check ((Get-FileHash $path).Hash -eq $hash) 'Repeated output never overwrites.'
  Assert-MsiContents $path "$fixture/payload" $config '0.1.0' $arch "$fixture/inspect-$arch"
  New-MsiPreflightCopy $path "$fixture/isolated-$arch.msi"
  $isolated=Read-MsiPackage "$fixture/isolated-$arch.msi"
  Check ($isolated.Properties.ProductCode -ne $info.Properties.ProductCode -and $isolated.Properties.UpgradeCode -ne $info.Properties.UpgradeCode) 'Preflight identity isolation.'
  Check ($isolated.PackageCode -ne $info.PackageCode) 'Preflight package isolation.'
  Check (@($isolated.Components | Where-Object ComponentId -in @($info.Components.ComponentId)).Count -eq 0) 'Preflight component isolation.'
  Check ((Get-FileHash $path).Hash -eq $hash) 'Inspection preserves original bytes.'
  $prior[$arch]=$info
 }
 Check ($prior.x64.Properties.UpgradeCode -ne $prior.ARM64.Properties.UpgradeCode) 'Architecture upgrade isolation.'
 New-MsiPackage $config "$fixture/payload" "$fixture/new.msi" '0.2.0' 'x64'
 $new=Read-MsiPackage "$fixture/new.msi"
 Check ($new.Properties.ProductCode -ne $prior.x64.Properties.ProductCode) 'Version changes product code.'
 Check ($new.Properties.UpgradeCode -eq $prior.x64.Properties.UpgradeCode) 'Upgrade identity stable.'
 Check ($new.Components[0].ComponentId -eq $prior.x64.Components[0].ComponentId) 'Component identity stable across versions.'
 # Reject even an empty custom-action table before opening a native session.
 Copy-Item "$fixture/x64.msi" "$fixture/blocked.msi"
 $installer=$null; $database=$null
 try {
  $installer=New-Object -ComObject WindowsInstaller.Installer
  $database=$installer.OpenDatabase("$fixture/blocked.msi",1)
  Invoke-MsiSql $database 'CREATE TABLE `CustomAction` (`Action` CHAR(72) NOT NULL PRIMARY KEY `Action`)'
  $null=$database.Commit()
 } finally { Close-MsiCom $database; Close-MsiCom $installer }
 $before=(Get-FileHash "$fixture/blocked.msi").Hash
 Rejected { Assert-MsiPreflight "$fixture/blocked.msi" }
 Check ((Get-FileHash "$fixture/blocked.msi").Hash -eq $before) 'Rejected preflight never changes original bytes.'
 $config.product.upgradeCode=[guid]::NewGuid().ToString('B'); $config.product.name='Second Product'; $config.product.id='second'
 New-MsiPackage $config "$fixture/payload" "$fixture/second.msi" '0.1.0' 'x64'
 $second=Read-MsiPackage "$fixture/second.msi"
 Check ($second.Properties.UpgradeCode -ne $prior.x64.Properties.UpgradeCode -and $second.Components[0].ComponentId -ne $prior.x64.Components[0].ComponentId) 'Product identity isolation.'
 # Copy only the engine outside the repository and invoke its public command.
 Copy-Item -LiteralPath $engine -Destination "$fixture/standalone engine" -Recurse
 $config | ConvertTo-Json -Depth 10 | Set-Content "$fixture/second.json"
 Push-Location $fixture
 try {
  $result=& './standalone engine/package.ps1' -Config './second.json' -PayloadDirectory './payload' -Version '0.3.0' -Architecture x64 -OutputDirectory './public output'
 } finally { Pop-Location }
 Check (Test-Path $result.Artifact) 'Standalone public command works.'
 Check ($result.SHA256 -eq (Get-FileHash $result.Artifact).Hash) 'Public result identifies final bytes.'
 Write-Host "Native MSI checks passed: $checks (synthetic files; no product installed)."
} finally {
 $target=[IO.Path]::GetFullPath($fixture)
 if([IO.Path]::GetDirectoryName($target) -ne [IO.Path]::GetTempPath().TrimEnd('\') -or [IO.Path]::GetFileName($target) -notlike 'GenericMsi-*') { throw 'Unsafe MSI test cleanup.' }
 Remove-Item -LiteralPath $target -Recurse -Force
}
