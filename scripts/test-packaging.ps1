#requires -Version 7.4
$ErrorActionPreference='Stop'
$helper=Join-Path $PSScriptRoot 'packaging/Common.ps1'
if(Test-Path $helper) { . $helper }
if(!(Get-Command Get-AgendaVersion -ErrorAction SilentlyContinue)) { throw 'Missing Agenda packaging adapter.' }
function Check([bool]$Value,[string]$Message) { if(!$Value) { throw $Message } }
function Rejected([scriptblock]$Action) { $failed=$false; try { & $Action | Out-Null } catch { $failed=$true }; Check $failed 'Expected packaging rejection.' }
$root=Split-Path $PSScriptRoot -Parent
[xml]$props=Get-Content (Join-Path $root 'Directory.Build.props') -Raw
Check ((Get-AgendaVersion $root) -eq [string]@($props.Project.PropertyGroup.AgendaVersion | Where-Object {$_})[0]) 'Central version.'
$fixture=Join-Path ([IO.Path]::GetTempPath()) ('AgendaPackaging-'+[guid]::NewGuid().ToString('N'))
$null=New-Item -ItemType Directory -Path $fixture
try {
 Rejected { Assert-AgendaPayload $fixture }
 $script:AgendaToolLog=Join-Path $fixture 'tool-output.log'
 $pwsh=(Get-Process -Id $PID).Path
 Invoke-AgendaTool $pwsh @('-NoProfile','-Command',"Write-Output 'tool-log-fixture'; exit 0")
 Check ((Test-Path -LiteralPath $script:AgendaToolLog) -and (Get-Content -LiteralPath $script:AgendaToolLog -Raw).Contains('tool-log-fixture')) 'Packaging tool output is preserved in a log.'
 foreach($file in @('Agenda.App.exe','Agenda.App.dll','Agenda.App.pri','App.xbf','MainWindow.xbf','Microsoft.WindowsAppRuntime.Insights.Resource.dll')) { [IO.File]::WriteAllText("$fixture/$file",'synthetic') }
 Assert-AgendaPayload $fixture
 $artifact="$fixture/test.msi"; [IO.File]::WriteAllText($artifact,'before signing')
 function Invoke-AgendaSignature { throw 'Synthetic signing failure' }
 Rejected { Complete-AgendaArtifact -Artifact $artifact -Version '0.4.0' -Commit ('a'*40) -Format Msi -Architecture x64 -Sign -CertificatePath missing }
 Check (!(Test-Path "$fixture/release-artifacts.json")) 'Failed signing emits no success manifest.'
 function Invoke-AgendaSignature { [IO.File]::AppendAllText($artifact,'signed fixture') }
 $result=Complete-AgendaArtifact -Artifact $artifact -Version '0.4.0' -Commit ('a'*40) -Format Msi -Architecture x64 -Sign -CertificatePath synthetic
 $manifest=Get-Content $result.Manifest -Raw | ConvertFrom-Json
 Check ($manifest.Signed -and $manifest.SHA256 -eq (Get-FileHash $artifact).Hash) 'Hash describes signed bytes.'
 $scriptFile=Join-Path $PSScriptRoot 'package.ps1'
 Rejected { & $scriptFile -Format Msi -UseCertificateStore }
 $tokens=$null; $errors=$null
 $ast=[Management.Automation.Language.Parser]::ParseFile($scriptFile,[ref]$tokens,[ref]$errors)
 Check (!$errors) 'Package script parses.'
 $format=@($ast.ParamBlock.Parameters | Where-Object { $_.Name.VariablePath.UserPath -eq 'Format' })[0]
 Check ($format.DefaultValue.Value -eq 'Msi') 'Packaging defaults to MSI.'
 $unsigned="$fixture/unsigned.msix"; [IO.File]::WriteAllText($unsigned,'synthetic unsigned package')
 function Invoke-AgendaSignature { throw 'Unsigned packaging must never call the signer.' }
 $unsignedResult=Complete-AgendaArtifact -Artifact $unsigned -Version '0.4.0' -Commit ('a'*40) -Format Msix -Architecture x64
 $unsignedManifest=Get-Content $unsignedResult.Manifest -Raw | ConvertFrom-Json
 Check (!$unsignedManifest.Signed -and $unsignedManifest.SHA256 -eq (Get-FileHash $unsigned).Hash) 'Explicit MSIX can remain unsigned with correct final metadata.'
 . (Join-Path $PSScriptRoot 'packaging/Signing.ps1')
 Assert-AgendaSigningOptions
 Assert-AgendaSigningOptions -CertificateThumbprint ('a'*40)
 Rejected { Assert-AgendaSigningOptions -CertificateThumbprint 'invalid' }
 Rejected { Assert-AgendaSigningOptions -CertificateThumbprint ('a'*40) -CertificatePath example.pfx }
 Rejected { Assert-AgendaSigningOptions -CertificatePassword orphan }
 Rejected { Assert-AgendaSigningOptions -TimestampUri 'https://timestamp.example.test' }
 Rejected { Assert-AgendaSigningOptions -CertificateThumbprint ('a'*40) -TimestampUri 'http://timestamp.example.test' }
 $metadata=[pscustomobject]@{Subject='CN=Agenda.Development';HasPrivateKey=$true;NotBefore=(Get-Date).AddDays(-1);NotAfter=(Get-Date).AddDays(1);UsageOids=@('1.3.6.1.5.5.7.3.3')}
 Assert-AgendaSigningMetadata $metadata 'CN=Agenda.Development'
 Rejected { Assert-AgendaSigningMetadata $metadata 'CN=Other Publisher' }
 $metadata.HasPrivateKey=$false
 Rejected { Assert-AgendaSigningMetadata $metadata 'CN=Agenda.Development' }
 $metadata.HasPrivateKey=$true; $metadata.NotAfter=(Get-Date).AddDays(-1)
 Rejected { Assert-AgendaSigningMetadata $metadata 'CN=Agenda.Development' }
 $metadata.NotAfter=(Get-Date).AddDays(1); $metadata.UsageOids=@()
 Rejected { Assert-AgendaSigningMetadata $metadata 'CN=Agenda.Development' }
 Write-Host 'Agenda packaging checks passed.'
} finally {
 $target=[IO.Path]::GetFullPath($fixture)
 if([IO.Path]::GetDirectoryName($target) -ne [IO.Path]::GetTempPath().TrimEnd('\') -or [IO.Path]::GetFileName($target) -notlike 'AgendaPackaging-*') { throw 'Unsafe packaging test cleanup.' }
 Remove-Item -LiteralPath $target -Recurse -Force
}
