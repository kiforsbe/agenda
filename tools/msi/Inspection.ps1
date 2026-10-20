#requires -Version 7.4
function Read-MsiPackage([string]$Path) {
 $installer=$null;$database=$null;$summary=$null
 try {
  $installer=New-Object -ComObject WindowsInstaller.Installer;$database=$installer.OpenDatabase($Path,0)
  $properties=@{}; foreach($row in Get-MsiRows $database Property @('Property','Value')) { $properties[$row.Property]=$row.Value }
  $tables=@(Get-MsiRows $database _Tables @('Name') | ForEach-Object Name)
  $summary=$database.SummaryInformation(0)
  [pscustomobject]@{Tables=$tables;FolderComponents=@(Get-MsiRows $database CreateFolder @('Directory_','Component_'));Removals=@(Get-MsiRows $database RemoveFile @('FileKey','Component_','FileName','DirProperty','InstallMode'));FeatureComponents=@(Get-MsiRows $database FeatureComponents @('Feature_','Component_'));Properties=$properties;PackageCode=$summary.Property(9);Template=$summary.Property(7);Files=@(Get-MsiRows $database File @('File','Component_','FileName','FileSize','Version','Sequence'));Components=@(Get-MsiRows $database Component @('Component','ComponentId','Directory_','Attributes','KeyPath'));Directories=@(Get-MsiRows $database Directory @('Directory','Directory_Parent','DefaultDir'));CustomActionCount=if('CustomAction' -in $tables) {@(Get-MsiRows $database CustomAction @('Action')).Count}else{0};RegistryCount=if('Registry' -in $tables){@(Get-MsiRows $database Registry @('Registry')).Count}else{0};Shortcuts=@(Get-MsiRows $database Shortcut @('Shortcut','Directory_','Name','Component_','Target'));ShortcutTarget=@(Get-MsiRows $database Shortcut @('Target'))[0].Target;UpgradeCount=@(Get-MsiRows $database Upgrade @('UpgradeCode')).Count;LaunchConditions=(@(Get-MsiRows $database LaunchCondition @('Condition')) | ForEach-Object Condition) -join ';';Actions=@(Get-MsiRows $database InstallExecuteSequence @('Action','Sequence'));Media=@(Get-MsiRows $database Media @('DiskId','LastSequence','Cabinet'))}
 } finally { Close-MsiCom $summary;Close-MsiCom $database;Close-MsiCom $installer }
}

# Read an embedded stream through msi.dll rather than opening an install session.
# Native handles are closed on every failure path; extracted bytes are bounded.
function Export-MsiCabinet([string]$Path,[string]$Destination) {
 if(!('ReusableMsi.Packaging.MsiStream' -as [type])) {
  Add-Type -TypeDefinition @'
using System;
using System.IO;
using System.ComponentModel;
using System.Runtime.InteropServices;
[assembly:DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
namespace ReusableMsi.Packaging {
 public static class MsiStream {
  [DllImport("msi.dll",CharSet=CharSet.Unicode)] static extern uint MsiOpenDatabase(string path,IntPtr mode,out uint handle);
  [DllImport("msi.dll",CharSet=CharSet.Unicode)] static extern uint MsiDatabaseOpenView(uint db,string query,out uint view);
  [DllImport("msi.dll")] static extern uint MsiViewExecute(uint view,uint record);
  [DllImport("msi.dll")] static extern uint MsiViewFetch(uint view,out uint record);
  [DllImport("msi.dll")] static extern uint MsiRecordReadStream(uint record,uint field,byte[] data,ref uint count);
  [DllImport("msi.dll")] static extern uint MsiCloseHandle(uint handle);
  static void Check(uint result) { if(result!=0) throw new Win32Exception((int)result); }
  public static void Copy(string source,string destination) {
   uint db=0,view=0,record=0;
   try {
    Check(MsiOpenDatabase(source,IntPtr.Zero,out db));
    Check(MsiDatabaseOpenView(db,"SELECT `Data` FROM `_Streams` WHERE `Name`='payload.cab'",out view));
    Check(MsiViewExecute(view,0));Check(MsiViewFetch(view,out record));
    using(var output=new FileStream(destination,FileMode.CreateNew,FileAccess.Write)) {
     var buffer=new byte[65536]; long total=0;
     while(true) { uint count=(uint)buffer.Length;Check(MsiRecordReadStream(record,1,buffer,ref count));if(count==0) break;
      total+=count;if(total>2147483648L) throw new InvalidDataException("MSI cabinet exceeds inspection limit.");output.Write(buffer,0,(int)count); }
    }
   } finally { if(record!=0) MsiCloseHandle(record);if(view!=0) MsiCloseHandle(view);if(db!=0) MsiCloseHandle(db); }
  }
 }
}
'@
 }
 [ReusableMsi.Packaging.MsiStream]::Copy($Path,$Destination)
}
# The engine must see a disposable, unregistered identity; never edit the deliverable.
function New-MsiPreflightCopy([string]$Path,[string]$Destination) {
 $ErrorActionPreference='Stop'
 $Path=[IO.Path]::GetFullPath($Path);$Destination=[IO.Path]::GetFullPath($Destination)
 if(!(Test-Path -LiteralPath $Path -PathType Leaf) -or (Test-Path -LiteralPath $Destination)) { throw 'Preflight needs an existing MSI and a new disposable destination.' }
 Copy-Item -LiteralPath $Path -Destination $Destination
 $engine=$null;$database=$null;$summary=$null;$view=$null;$record=$null
 try {
  $engine=New-Object -ComObject WindowsInstaller.Installer
  $database=$engine.OpenDatabase($Destination,1)
  $tables=@(Get-MsiRows $database _Tables @('Name') | ForEach-Object Name)
  if('CustomAction' -in $tables) { throw 'Read-only MSI preflight does not permit custom actions.' }
  foreach($property in @('ProductCode','UpgradeCode')) {
   $guid=[guid]::NewGuid().ToString('B').ToUpperInvariant()
   Invoke-MsiSql $database ("UPDATE ``Property`` SET ``Value``='$guid' WHERE ``Property``='$property'")
   if($property -eq 'UpgradeCode') {
    $columns=@('UpgradeCode','VersionMin','VersionMax','Language','Attributes','Remove','ActionProperty')
    $upgrades=@(Get-MsiRows $database Upgrade $columns)
    Invoke-MsiSql $database 'DELETE FROM `Upgrade`'
    foreach($upgrade in $upgrades) {
     Add-MsiRow $engine $database Upgrade $columns @($guid,$upgrade.VersionMin,$upgrade.VersionMax,$upgrade.Language,[int]$upgrade.Attributes,$upgrade.Remove,$upgrade.ActionProperty)
    }
   }
  }
  $view=$database.OpenView('UPDATE `Component` SET `ComponentId`=? WHERE `Component`=?')
  $record=$engine.CreateRecord(2)
  foreach($component in @(Get-MsiRows $database Component @('Component'))) {
   $record.StringData(1)=[guid]::NewGuid().ToString('B').ToUpperInvariant()
   $record.StringData(2)=$component.Component
   $null=$view.Execute($record)
  }
  $summary=$database.SummaryInformation(20)
  $summary.Property(9)=[guid]::NewGuid().ToString('B').ToUpperInvariant()
  $null=$summary.Persist();$null=$database.Commit()
 } finally {
  Close-MsiCom $record
  if($view) { $null=$view.Close();Close-MsiCom $view }
  Close-MsiCom $summary;Close-MsiCom $database;Close-MsiCom $engine
 }
}
# Explicit read-only preflight actions only; never initialize or execute an install transaction here.
function Assert-MsiPreflight([string]$Path) {
 $ErrorActionPreference='Stop'
 # OpenPackage requires native Windows separators, unlike OpenDatabase.
 $Path=[IO.Path]::GetFullPath($Path)
 $tempRoot=[IO.Path]::GetFullPath([IO.Path]::GetTempPath())
 $preflightRoot=Join-Path $tempRoot ('Generic-MsiPreflight-'+[guid]::NewGuid().ToString('N'))
 $null=New-Item -ItemType Directory -Path $preflightRoot
 $copy=Join-Path $preflightRoot 'preflight.msi'
 $engine=$null;$session=$null;$previousUi=$null
 try {
  New-MsiPreflightCopy $Path $copy
  $engine=New-Object -ComObject WindowsInstaller.Installer
  $previousUi=$engine.UILevel;$engine.UILevel=2
  # IgnoreMachineState=1 creates a restricted engine that cannot run InstallValidate.
  $session=$engine.OpenPackage($copy,0)
  # Cost only disposable paths, never installed application or Start-menu files.
  $session.Property('INSTALLFOLDER')=Join-Path $preflightRoot 'target'
  $session.Property('APPMENUDIR')=Join-Path $preflightRoot 'menu'
  foreach($action in @('AppSearch','CostInitialize','FileCost','CostFinalize','InstallValidate')) {
   if($session.DoAction($action) -ne 1) { throw "MSI native read-only preflight failed at $action." }
  }
  if($session.Property('APPWINDOWSBUILD') -notmatch '^\d+$') { throw 'MSI native AppSearch did not resolve the Windows build.' }
 } finally {
  Close-MsiCom $session
  if($engine -and $null -ne $previousUi) { $engine.UILevel=$previousUi }
  Close-MsiCom $engine
  $target=[IO.Path]::GetFullPath($preflightRoot)
  if(!$target.StartsWith($tempRoot,[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($target) -notlike 'Generic-MsiPreflight-*') { throw 'Unsafe MSI preflight cleanup path.' }
  Remove-Item -LiteralPath $target -Recurse -Force
 }
}
# Compare authored table mappings AND cabinet bytes. A valid File table alone
# cannot establish that the embedded cabinet contains the expected application.
function Assert-MsiContents([string]$Path,[string]$Folder,$Configuration,[string]$Version,[string]$Architecture,[string]$InspectionFolder) {
 $expected=@(Get-MsiPayload $Configuration $Folder $Architecture);$actual=Read-MsiPackage $Path
 if($actual.Properties.ProductVersion -cne $Version -or $actual.Properties.ProductCode -ne (Get-MsiPackageGuid $Configuration $Architecture "product/$Version") -or $actual.Properties.UpgradeCode -ne (Get-MsiPackageGuid $Configuration $Architecture 'upgrade') -or $actual.Template -cne $(if($Architecture -eq 'ARM64'){'Arm64;1033'}else{'x64;1033'}) -or $actual.Properties.ALLUSERS -ne '1') { throw 'MSI identity/version/scope mismatch.' }
 if('Signature' -notin $actual.Tables -or 'CreateFolder' -notin $actual.Tables) { throw 'MSI is missing required search/folder tables.' }
 if('CustomAction' -in $actual.Tables -or 'Registry' -in $actual.Tables) { throw 'MSI inspection rejects custom-action and registry authoring tables.' }
 if($actual.Shortcuts.Count -ne $Configuration.shortcuts.Count) { throw 'MSI shortcut count differs from configuration.' }
 foreach($shortcut in $Configuration.shortcuts) {
  $matches=@($actual.Shortcuts | Where-Object { ($_.Name -split '\|')[-1] -ceq $shortcut.name -and $_.Target -ceq 'Main' -and $_.Directory_ -ceq 'APPMENUDIR' })
  if($matches.Count -ne 1) { throw 'MSI configured shortcut mapping mismatch.' }
 }
 if($actual.CustomActionCount -or $actual.RegistryCount -or $actual.Files.Count -ne $expected.Count -or $actual.Components.Count -ne $expected.Count -or $actual.FeatureComponents.Count -ne $expected.Count -or $actual.UpgradeCount -ne 2 -or $actual.ShortcutTarget -cne 'Main') { throw 'MSI contains unexpected actions, files, components or shortcuts.' }
 if($actual.Media.Count -ne 1 -or $actual.Media[0].Cabinet -cne '#payload.cab' -or [int]$actual.Media[0].LastSequence -ne $expected.Count) { throw 'MSI must contain exactly one embedded cabinet.' }
 $main=@($expected | Where-Object Relative -eq $Configuration.application.executable.Replace('\','/'))[0]
 $ownedDirectories=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
 $null=$ownedDirectories.Add('INSTALLFOLDER');$null=$ownedDirectories.Add('APPMENUDIR')
 foreach($file in $expected) {
  $scope=''
  foreach($segment in @([IO.Path]::GetDirectoryName($file.Relative).Replace('\','/').Split('/') | Where-Object { $_ })) {
   $scope=if($scope){$scope+'/'+$segment}else{$segment}
   $null=$ownedDirectories.Add((Get-MsiPackageId $Configuration $Architecture 'D' $scope))
  }
 }
 if($actual.FolderComponents.Count -ne $ownedDirectories.Count -or @($actual.FolderComponents | Where-Object { !$ownedDirectories.Contains($_.Directory_) -or $_.Component_ -cne $main.Component }).Count) { throw 'MSI folder ownership must cover only owned install/menu directories.' }
 if($actual.Removals.Count -ne 1 -or $actual.Removals[0].FileName -or $actual.Removals[0].DirProperty -cne 'APPMENUDIR' -or $actual.Removals[0].Component_ -cne $main.Component -or $actual.Removals[0].InstallMode -ne '2') { throw 'MSI explicit cleanup must remove only the owned empty menu folder.' }
 if(($Architecture -eq 'x64' -and [Runtime.InteropServices.RuntimeInformation]::OSArchitecture -eq 'X64') -or ($Architecture -eq 'ARM64' -and [Runtime.InteropServices.RuntimeInformation]::OSArchitecture -eq 'Arm64')) { Assert-MsiPreflight $Path }
 $null=New-Item -ItemType Directory -Path $InspectionFolder
 $cab=Join-Path $InspectionFolder 'payload.cab';$extracted=Join-Path $InspectionFolder 'files';$null=New-Item -ItemType Directory -Path $extracted
 Export-MsiCabinet $Path $cab
 Invoke-MsiTool (Join-Path $env:SystemRoot 'System32/expand.exe') @('-F:*',$cab,$extracted)
 if(@(Get-ChildItem -LiteralPath $extracted -File).Count -ne $expected.Count) { throw 'Embedded MSI cabinet file count mismatch.' }
 foreach($file in $expected) {
  $sequence=[array]::IndexOf($expected,$file)+1
  $row=@($actual.Files | Where-Object File -eq $file.Id)
  $component=@($actual.Components | Where-Object Component -eq $file.Component)
  if(@($actual.FeatureComponents | Where-Object { $_.Feature_ -ceq 'Main' -and $_.Component_ -ceq $file.Component }).Count -ne 1) { throw 'MSI payload component is not bound to the removable main feature.' }
  $dir=[IO.Path]::GetDirectoryName($file.Relative).Replace('\','/')
  $expectedDir=if($dir) {Get-MsiPackageId $Configuration $Architecture 'D' $dir}else{'INSTALLFOLDER'}
  if($row.Count -ne 1 -or [int]$row[0].Sequence -ne $sequence -or ($row[0].FileName -split '\|')[-1] -cne $file.Name -or [long]$row[0].FileSize -ne $file.Length -or $component.Count -ne 1 -or $row[0].Component_ -cne $file.Component -or $component[0].ComponentId -cne (Get-MsiPackageGuid $Configuration $Architecture ('component/'+$file.Relative)) -or $component[0].KeyPath -cne $file.Id -or [int]$component[0].Attributes -ne 256 -or $component[0].Directory_ -cne $expectedDir) { throw 'MSI file/component mapping mismatch.' }
  if((Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash -cne (Get-FileHash -LiteralPath (Join-Path $extracted $file.Id) -Algorithm SHA256).Hash) { throw 'MSI embedded payload hash mismatch.' }
 }
}
