#requires -Version 7.4
function New-MsiPackage {
 # Author standard tables/actions only: no executable custom actions, product
 # installation, trust changes or automatic application launch.
 param($Configuration,[string]$Folder,[string]$Path,[string]$Version,[ValidateSet('x64','ARM64')][string]$Architecture)
 Assert-MsiVersion $Version
 if(Test-Path -LiteralPath $Path) { throw 'Refusing to overwrite an existing MSI.' }
 $files=@(Get-MsiPayload $Configuration $Folder $Architecture)
 $work=Join-Path (Split-Path $Path -Parent) ('msi-authoring-'+[guid]::NewGuid().ToString('N'))
 $null=New-Item -ItemType Directory -Path $work
 $cab=Join-Path $work 'payload.cab';$ddf=Join-Path $work 'payload.ddf'
 $directives=@('.OPTION EXPLICIT','.Set Cabinet=on','.Set Compress=on','.Set CompressionType=LZX','.Set CompressionMemory=21','.Set MaxDiskSize=0','.Set CabinetNameTemplate=payload.cab',('.Set DiskDirectoryTemplate="'+$work+'"'))
 $directives+=@($files | ForEach-Object { '"'+$_.FullName+'" '+$_.Id })
 $directives=@(('.Set InfFileName="'+(Join-Path $work 'setup.inf')+'"'),('.Set RptFileName="'+(Join-Path $work 'setup.rpt')+'"'))+$directives
 [IO.File]::WriteAllLines($ddf,$directives,[Text.Encoding]::UTF8)
 Invoke-MsiTool (Join-Path $env:SystemRoot 'System32/makecab.exe') @('/F',$ddf)
 $installer=$null;$database=$null;$summary=$null;$failedPath=$null
 try {
  $installer=New-Object -ComObject WindowsInstaller.Installer
  $database=$installer.OpenDatabase($Path,3)
  $schemas=@{
   Property='`Property` CHAR(72) NOT NULL, `Value` CHAR(0) LOCALIZABLE PRIMARY KEY `Property`'
   Directory='`Directory` CHAR(72) NOT NULL, `Directory_Parent` CHAR(72), `DefaultDir` CHAR(255) NOT NULL LOCALIZABLE PRIMARY KEY `Directory`'
   Component='`Component` CHAR(72) NOT NULL, `ComponentId` CHAR(38), `Directory_` CHAR(72) NOT NULL, `Attributes` SHORT NOT NULL, `Condition` CHAR(255), `KeyPath` CHAR(72) PRIMARY KEY `Component`'
   Feature='`Feature` CHAR(38) NOT NULL, `Feature_Parent` CHAR(38), `Title` CHAR(64) LOCALIZABLE, `Description` CHAR(255) LOCALIZABLE, `Display` SHORT, `Level` SHORT NOT NULL, `Directory_` CHAR(72), `Attributes` SHORT NOT NULL PRIMARY KEY `Feature`'
   FeatureComponents='`Feature_` CHAR(38) NOT NULL, `Component_` CHAR(72) NOT NULL PRIMARY KEY `Feature_`, `Component_`'
   File='`File` CHAR(72) NOT NULL, `Component_` CHAR(72) NOT NULL, `FileName` CHAR(255) NOT NULL LOCALIZABLE, `FileSize` LONG NOT NULL, `Version` CHAR(72), `Language` CHAR(20), `Attributes` SHORT, `Sequence` LONG NOT NULL PRIMARY KEY `File`'
   Media='`DiskId` SHORT NOT NULL, `LastSequence` LONG NOT NULL, `DiskPrompt` CHAR(64) LOCALIZABLE, `Cabinet` CHAR(255), `VolumeLabel` CHAR(32), `Source` CHAR(72) PRIMARY KEY `DiskId`'
   Upgrade='`UpgradeCode` CHAR(38) NOT NULL, `VersionMin` CHAR(20), `VersionMax` CHAR(20), `Language` CHAR(255), `Attributes` LONG NOT NULL, `Remove` CHAR(255), `ActionProperty` CHAR(72) NOT NULL PRIMARY KEY `UpgradeCode`, `VersionMin`, `VersionMax`, `Language`, `Attributes`'
   LaunchCondition='`Condition` CHAR(255) NOT NULL, `Description` CHAR(255) NOT NULL LOCALIZABLE PRIMARY KEY `Condition`'
   # AppSearch requires this table even when RegLocator reads a raw registry value.
   Signature='`Signature` CHAR(72) NOT NULL, `FileName` CHAR(255) NOT NULL, `MinVersion` CHAR(20), `MaxVersion` CHAR(20), `MinSize` LONG, `MaxSize` LONG, `MinDate` LONG, `MaxDate` LONG, `Languages` CHAR(255) PRIMARY KEY `Signature`'
   AppSearch='`Property` CHAR(72) NOT NULL, `Signature_` CHAR(72) NOT NULL PRIMARY KEY `Property`, `Signature_`'
   RegLocator='`Signature_` CHAR(72) NOT NULL, `Root` SHORT, `Key` CHAR(255) NOT NULL, `Name` CHAR(255), `Type` SHORT PRIMARY KEY `Signature_`'
   Shortcut='`Shortcut` CHAR(72) NOT NULL, `Directory_` CHAR(72) NOT NULL, `Name` CHAR(128) NOT NULL LOCALIZABLE, `Component_` CHAR(72) NOT NULL, `Target` CHAR(255) NOT NULL, `Arguments` CHAR(255), `Description` CHAR(255) LOCALIZABLE, `Hotkey` SHORT, `Icon_` CHAR(72), `IconIndex` SHORT, `ShowCmd` SHORT, `WkDir` CHAR(72) PRIMARY KEY `Shortcut`'
   CreateFolder='`Directory_` CHAR(72) NOT NULL, `Component_` CHAR(72) NOT NULL PRIMARY KEY `Directory_`, `Component_`'
   RemoveFile='`FileKey` CHAR(72) NOT NULL, `Component_` CHAR(72) NOT NULL, `FileName` CHAR(255) LOCALIZABLE, `DirProperty` CHAR(72) NOT NULL, `InstallMode` SHORT NOT NULL PRIMARY KEY `FileKey`'
   Icon='`Name` CHAR(72) NOT NULL, `Data` OBJECT NOT NULL PRIMARY KEY `Name`'
   MsiFileHash='`File_` CHAR(72) NOT NULL, `Options` SHORT NOT NULL, `HashPart1` LONG NOT NULL, `HashPart2` LONG NOT NULL, `HashPart3` LONG NOT NULL, `HashPart4` LONG NOT NULL PRIMARY KEY `File_`'
   InstallExecuteSequence='`Action` CHAR(72) NOT NULL, `Condition` CHAR(255), `Sequence` SHORT PRIMARY KEY `Action`'
   InstallUISequence='`Action` CHAR(72) NOT NULL, `Condition` CHAR(255), `Sequence` SHORT PRIMARY KEY `Action`'
  }
  foreach($table in $schemas.Keys) { Invoke-MsiSql $database ('CREATE TABLE `{0}` ({1})' -f $table,$schemas[$table]) }
  # ProductCode changes with version. UpgradeCode stays stable within each
  # architecture; PackageCode below is fresh even when the payload is identical.
  $properties=@{ProductName=$Configuration.product.name;ProductVersion=$Version;ProductLanguage='1033';Manufacturer=$Configuration.product.manufacturer;ProductCode=(Get-MsiPackageGuid $Configuration $Architecture "product/$Version");UpgradeCode=(Get-MsiPackageGuid $Configuration $Architecture 'upgrade');ALLUSERS='1';INSTALLLEVEL='1';ARPNOMODIFY='1';REBOOT='ReallySuppress';MSIRESTARTMANAGERCONTROL='Disable';SecureCustomProperties='OLDERVERSIONS;NEWERVERSIONS;APPWINDOWSBUILD'}
  foreach($name in $properties.Keys) { Add-MsiRow $installer $database Property @('Property','Value') @($name,$properties[$name]) }
  # Architecture families have different components; never let them own the same files.
  $directoryName=$Configuration.installation.directoryName+' ('+$Architecture.ToLowerInvariant()+')'
  # Filename columns need 8.3|long pairs even on long-name Windows filesystems.
  $installShort='A'+(Get-MsiPackageId $Configuration $Architecture 'D' 'install-root').Substring(1,7)
  $menuShort='M'+(Get-MsiPackageId $Configuration $Architecture 'D' 'menu-root').Substring(1,7)
  foreach($directory in @(@('TARGETDIR',$null,'SourceDir'),@('ProgramFiles64Folder','TARGETDIR','PFiles64'),@('INSTALLFOLDER','ProgramFiles64Folder',($installShort+'|'+$directoryName)),@('ProgramMenuFolder','TARGETDIR','PMenu'),@('APPMENUDIR','ProgramMenuFolder',($menuShort+'|'+$directoryName)))) {
   Add-MsiRow $installer $database Directory @('Directory','Directory_Parent','DefaultDir') $directory
  }
  $directories=@{''='INSTALLFOLDER'}
  $shortNames=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
  foreach($file in $files) {
   $relativeParent=[IO.Path]::GetDirectoryName($file.Relative).Replace('\','/')
   $parent='INSTALLFOLDER';$scope=''
   foreach($segment in @($relativeParent.Split('/') | Where-Object { $_ })) {
    $scope=if($scope) { $scope+'/'+$segment } else { $segment }
    if(!$directories.ContainsKey($scope)) {
     $id=Get-MsiPackageId $Configuration $Architecture 'D' $scope
     Add-MsiRow $installer $database Directory @('Directory','Directory_Parent','DefaultDir') @($id,$parent,('D'+$id.Substring(1,7)+'|'+$segment))
     if(!$shortNames.Add($parent+'/D'+$id.Substring(1,7))) { throw 'MSI directory short-name collision.' }
     $directories[$scope]=$id
    }
    $parent=$directories[$scope]
   }
   Add-MsiRow $installer $database Component @('Component','ComponentId','Directory_','Attributes','KeyPath') @($file.Component,(Get-MsiPackageGuid $Configuration $Architecture ('component/'+$file.Relative)),$parent,256,$file.Id)
   Add-MsiRow $installer $database FeatureComponents @('Feature_','Component_') @('Main',$file.Component)
   # MSI needs the fixed binary version, not a vendor's descriptive FileVersion string.
   $fileVersion=$installer.FileVersion($file.FullName,$false)
   if([string]::IsNullOrEmpty($fileVersion)) { $fileVersion=$null }
   $sequence=[array]::IndexOf($files,$file)+1
   $extension=[IO.Path]::GetExtension($file.Name)
   if($extension.Length -gt 4) { $extension='' }
   $short='F'+$file.Id.Substring(1,7)+$extension
   if(!$shortNames.Add($parent+'/'+$short)) { throw 'MSI file short-name collision.' }
   Add-MsiRow $installer $database File @('File','Component_','FileName','FileSize','Version','Attributes','Sequence') @($file.Id,$file.Component,($short+'|'+$file.Name),[int]$file.Length,$fileVersion,512,$sequence)
   if(!$fileVersion) {
    $hash=$installer.FileHash($file.FullName,0)
    try { Add-MsiRow $installer $database MsiFileHash @('File_','Options','HashPart1','HashPart2','HashPart3','HashPart4') @($file.Id,0,$hash.IntegerData(1),$hash.IntegerData(2),$hash.IntegerData(3),$hash.IntegerData(4)) } finally { Close-MsiCom $hash }
   }
  }
  Add-MsiRow $installer $database Feature @('Feature','Title','Description','Display','Level','Directory_','Attributes') @('Main',$Configuration.product.name,$Configuration.product.name,1,1,'INSTALLFOLDER',0)
  Add-MsiRow $installer $database Media @('DiskId','LastSequence','Cabinet') @(1,$files.Count,'#payload.cab')
  # One row finds strictly older products for transactional removal; the other
  # finds same/newer products. Installed permits maintenance of this exact product.
  $upgrade=$properties.UpgradeCode
  Add-MsiRow $installer $database Upgrade @('UpgradeCode','VersionMax','Attributes','ActionProperty') @($upgrade,$Version,0,'OLDERVERSIONS')
  Add-MsiRow $installer $database Upgrade @('UpgradeCode','VersionMin','Attributes','ActionProperty') @($upgrade,$Version,258,'NEWERVERSIONS')
  Add-MsiRow $installer $database RegLocator @('Signature_','Root','Key','Name','Type') @('WindowsBuild',2,'SOFTWARE\Microsoft\Windows NT\CurrentVersion','CurrentBuildNumber',18)
  Add-MsiRow $installer $database AppSearch @('Property','Signature_') @('APPWINDOWSBUILD','WindowsBuild')
  Add-MsiRow $installer $database LaunchCondition @('Condition','Description') @(('Installed OR (VersionNT64 AND APPWINDOWSBUILD >= '+$Configuration.installation.minimumWindowsBuild+')'),('Requires Windows build '+$Configuration.installation.minimumWindowsBuild+' or later ('+$Architecture+').'))
  Add-MsiRow $installer $database LaunchCondition @('Condition','Description') @('Installed OR NOT NEWERVERSIONS','The same or a newer version is already installed.')
  $main=@($files | Where-Object Relative -eq $Configuration.application.executable.Replace('\','/'))[0]
  $icon=if($Configuration.application.icon) { Join-Path $Folder $Configuration.application.icon } else { $null }
  $iconName=$null
  if($icon -and (Test-Path -LiteralPath $icon)) {
   $iconName='App.exe'; Add-MsiRow $installer $database Icon @('Name','Data') @($iconName,$icon) @(1)
   Add-MsiRow $installer $database Property @('Property','Value') @('ARPPRODUCTICON',$iconName)
  }
  $shortcutIndex=0
  foreach($shortcut in $Configuration.shortcuts) {
   $shortcutShort='S'+(Get-MsiPackageId $Configuration $Architecture 'S' ('shortcut/'+$shortcut.name)).Substring(1,7)
   Add-MsiRow $installer $database Shortcut @('Shortcut','Directory_','Name','Component_','Target','Description','Icon_','ShowCmd','WkDir') @(('Shortcut'+(++$shortcutIndex)),'APPMENUDIR',($shortcutShort+'|'+$shortcut.name),$main.Component,'Main',$Configuration.product.name,$iconName,1,'INSTALLFOLDER')
  }
  Add-MsiRow $installer $database RemoveFile @('FileKey','Component_','DirProperty','InstallMode') @('RemoveStartMenuFolder',$main.Component,'APPMENUDIR',2)
  # Explicit ownership lets RemoveFolders clean only our empty install/menu directories.
  foreach($directoryId in @($directories.Values)+@('APPMENUDIR')) {
   Add-MsiRow $installer $database CreateFolder @('Directory_','Component_') @($directoryId,$main.Component)
  }

  # RemoveExistingProducts is after InstallInitialize so older-version removal
  # participates in the transaction. Unpublish/remove actions also support uninstall.
  # Do not add launch/termination hooks: keep product behavior outside the engine.
  $actions=@{FindRelatedProducts=25;AppSearch=50;LaunchConditions=100;ValidateProductID=700;CostInitialize=800;FileCost=900;CostFinalize=1000;MigrateFeatureStates=1200;InstallValidate=1400;InstallInitialize=1500;RemoveExistingProducts=1510;ProcessComponents=1600;UnpublishFeatures=1800;RemoveShortcuts=3200;RemoveFiles=3500;RemoveFolders=3600;CreateFolders=3700;InstallFiles=4000;CreateShortcuts=4500;RegisterUser=6000;RegisterProduct=6100;PublishFeatures=6300;PublishProduct=6400;InstallFinalize=6600}
  foreach($action in $actions.Keys) { Add-MsiRow $installer $database InstallExecuteSequence @('Action','Sequence') @($action,[int]$actions[$action]) }
  foreach($action in @('FindRelatedProducts','AppSearch','LaunchConditions','CostInitialize','FileCost','CostFinalize','MigrateFeatureStates')) { Add-MsiRow $installer $database InstallUISequence @('Action','Sequence') @($action,[int]$actions[$action]) }
  Add-MsiRow $installer $database InstallUISequence @('Action','Sequence') @('ExecuteAction',1300)
  Add-MsiRow $installer $database _Streams @('Name','Data') @('payload.cab',$cab) @(1)
  $null=$database.Commit(); Close-MsiCom $database; $database=$installer.OpenDatabase($Path,1)
  $summary=$database.SummaryInformation(20)
  # MSI summary metadata is separate from Property rows. The template must match
  # the native processor; 500 selects Installer 5.0, required for ARM64 support.
  $summary.Property(1)=1252; $summary.Property(2)=$Configuration.product.name+' installer'; $summary.Property(3)=$Configuration.product.name; $summary.Property(4)=$Configuration.product.manufacturer
  $summary.Property(7)=if($Architecture -eq 'ARM64'){'Arm64;1033'}else{'x64;1033'};$summary.Property(9)=[guid]::NewGuid().ToString('B').ToUpperInvariant();$summary.Property(14)=500;$summary.Property(15)=2;$summary.Property(18)='Generic MSI packaging'
  $null=$summary.Persist();$null=$database.Commit()
 } catch { if(Test-Path -LiteralPath $Path) { $failedPath=$Path }; throw }
 finally {
  Close-MsiCom $summary;Close-MsiCom $database;Close-MsiCom $installer
  if($failedPath -and (Test-Path -LiteralPath $failedPath)) { Remove-Item -LiteralPath $failedPath -Force }
 }
}
