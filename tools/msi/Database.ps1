#requires -Version 7.4
function Get-MsiPackageGuid($Configuration,[string]$Architecture,[string]$Scope) {
 return Get-MsiGuid $Configuration.product.upgradeCode "$Architecture/$Scope"
}
function Get-MsiPackageId($Configuration,[string]$Architecture,[string]$Prefix,[string]$Scope) {
 return Get-MsiId $Prefix $Configuration.product.upgradeCode "$Architecture/$Scope"
}
function Invoke-MsiTool([string]$Tool,[string[]]$Arguments) {
 $messages=@(& $Tool @Arguments 2>&1)
 if($LASTEXITCODE -ne 0) { throw "$([IO.Path]::GetFileName($Tool)) failed: $($messages -join [Environment]::NewLine)" }
 Write-Host "$([IO.Path]::GetFileName($Tool)): succeeded"
}
function Close-MsiCom($Object) {
 if($null -ne $Object -and [Runtime.InteropServices.Marshal]::IsComObject($Object)) { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($Object) }
}
function Invoke-MsiSql($Database,[string]$Sql) {
 $view=$null
 try { $view=$Database.OpenView($Sql); $null=$view.Execute() } finally { if($view) { $null=$view.Close(); Close-MsiCom $view } }
}
# Use parameterized records for all values, including localized names and streams;
# callers never interpolate configuration values into SQL statements.
function Add-MsiRow($Installer,$Database,[string]$Table,[string[]]$Columns,[object[]]$Values,[int[]]$Streams=@()) {
 if($Table -notmatch '^[A-Za-z_][A-Za-z0-9_]*$' -or @($Columns | Where-Object { $_ -notmatch '^[A-Za-z_][A-Za-z0-9_]*$' }).Count -or $Columns.Count -ne $Values.Count) { throw "Invalid MSI authoring row: $Table ($($Columns.Count) columns, $($Values.Count) values)." }
 $names=($Columns | ForEach-Object { '`'+$_+'`' }) -join ','
 $parameters=(@('?')*$Values.Count) -join ','
 $view=$null; $record=$null
 try {
  $view=$Database.OpenView(('INSERT INTO `{0}` ({1}) VALUES ({2})' -f $Table,$names,$parameters))
  $record=$Installer.CreateRecord($Values.Count)
  for($i=0;$i -lt $Values.Count;$i++) {
   if($null -eq $Values[$i]) { continue }
   if($i -in $Streams) { $null=$record.SetStream($i+1,[string]$Values[$i]) }
   elseif($Values[$i] -is [int] -or $Values[$i] -is [long]) { $record.IntegerData($i+1)=[int]$Values[$i] }
   else { $record.StringData($i+1)=[string]$Values[$i] }
  }
  $null=$view.Execute($record)
 } finally { Close-MsiCom $record; if($view) { $null=$view.Close();Close-MsiCom $view } }
}
# Bound reads protect inspection from unexpectedly large/malformed tables.
function Get-MsiRows($Database,[string]$Table,[string[]]$Columns) {
 $view=$null
 try {
  $names=($Columns | ForEach-Object { '`'+$_+'`' }) -join ','
  $view=$Database.OpenView(('SELECT {0} FROM `{1}`' -f $names,$Table));$null=$view.Execute()
  $count=0
  while($record=$view.Fetch()) {
   try {
    if(++$count -gt 10000) { throw 'MSI inspection row limit exceeded.' }
    $row=[ordered]@{}
    for($i=0;$i -lt $Columns.Count;$i++) { $row[$Columns[$i]]=$record.StringData($i+1) }
    [pscustomobject]$row
   } finally { Close-MsiCom $record }
  }
 } finally { if($view) { $null=$view.Close();Close-MsiCom $view } }
}
