#requires -Version 7.4
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$adapter=Join-Path $PSScriptRoot 'packaging/Agenda.ps1'
if(!(Test-Path -LiteralPath $adapter)) { throw 'Missing product-only Agenda adapter.' }
. $adapter
function Check([bool]$Value,[string]$Message) { if(!$Value) { throw $Message } }
function Rejected([scriptblock]$Action) { $failed=$false; try { & $Action | Out-Null } catch { $failed=$true }; Check $failed 'Expected packaging rejection.' }
$tools=Get-AgendaBuildToolsRoot $root
foreach($name in @('Invoke-WbtTool','Get-WbtSdkTool','Assert-WbtSigningOptions','Get-WbtSigningCertificate','Invoke-WbtSignature','Write-WbtArtifactManifest')) {
    $source=(Get-Command $name -CommandType Function).ScriptBlock.File
    Check ($source.StartsWith($tools,[StringComparison]::OrdinalIgnoreCase)) "$name must come from the external checkout."
}
foreach($file in @('packaging/Common.ps1','packaging/Installers.ps1','packaging/Signing.ps1','setup-dev-certificate.ps1','trust-dev-certificate.ps1')) {
    Check (!(Test-Path -LiteralPath (Join-Path $PSScriptRoot $file))) "Duplicate packaging helper remains: $file"
}
[xml]$props=Get-Content (Join-Path $root 'Directory.Build.props') -Raw
Check ((Get-AgendaVersion $root) -eq [string]@($props.Project.PropertyGroup.AgendaVersion | Where-Object {$_})[0]) 'Central product version.'
$fixture=Join-Path ([IO.Path]::GetTempPath()) ('AgendaPackaging-'+[guid]::NewGuid().ToString('N'))
$null=New-Item -ItemType Directory -Path "$fixture/unsigned" -Force
try {
    Rejected { Assert-AgendaPayload $fixture }
    foreach($file in @('Agenda.App.exe','Agenda.App.dll','Agenda.App.pri','App.xbf','MainWindow.xbf','Microsoft.WindowsAppRuntime.Insights.Resource.dll')) { [IO.File]::WriteAllText("$fixture/$file",'synthetic') }
    Assert-AgendaPayload $fixture
    $artifact="$fixture/test.msi"; [IO.File]::WriteAllText($artifact,'before signing')
    function Invoke-WbtSignature { throw 'Synthetic shared signing failure' }
    Rejected { Complete-AgendaArtifact -Artifact $artifact -Version 0.4.0 -Commit ('a'*40) -Format Msi -Architecture x64 -Sign -CertificatePath missing }
    Check (!(Test-Path "$fixture/release-artifacts.json")) 'Failed shared signing emits no success manifest.'
    # Stub the signing boundary only: real signature state must still come from
    # the shared writer inspecting the bytes, never the caller Sign switch.
    function Invoke-WbtSignature { param($Artifact) [IO.File]::AppendAllText($Artifact,'fixture mutation') }
    $result=Complete-AgendaArtifact -Artifact $artifact -Version 0.4.0 -Commit ('a'*40) -Format Msi -Architecture x64 -Sign -CertificatePath synthetic
    $manifest=Get-Content $result.Manifest -Raw | ConvertFrom-Json
    Check ($manifest.SHA256 -eq (Get-FileHash $artifact).Hash -and !$manifest.Signed) 'Shared writer describes actual final bytes and signature status.'
    Check ($manifest.SourceCommit -eq ('a'*40) -and $manifest.Commit -eq ('a'*40) -and $manifest.Format -eq 'Msi' -and $manifest.Architecture -eq 'x64') 'Product fields preserve shared metadata and Agenda compatibility.'
    Rejected { Complete-AgendaArtifact -Artifact $artifact -Version 0.4.0 -Commit ('a'*40) -Format Msi -Architecture x64 }
    $unsigned="$fixture/unsigned/app.msix"; [IO.File]::WriteAllText($unsigned,'synthetic unsigned bytes')
    function Invoke-WbtSignature { throw 'Unsigned packaging must not invoke signing.' }
    $result=Complete-AgendaArtifact -Artifact $unsigned -Version 0.4.0 -Commit ('b'*40) -Format Msix -Architecture ARM64 -SourceDirty
    $manifest=Get-Content $result.Manifest -Raw | ConvertFrom-Json
    Check (!$manifest.Signed -and $manifest.SourceDirty -and $manifest.Format -eq 'Msix' -and $manifest.Architecture -eq 'ARM64') 'Unsigned product metadata survives shared finalization.'
    $entry=Join-Path $PSScriptRoot 'package.ps1'
    Rejected { & $entry -CertificatePassword orphan }
    Rejected { & $entry -TimestampUri 'https://timestamp.example.test' }
    Rejected { & $entry -CertificateThumbprint invalid }
    Rejected { & $entry -CertificateThumbprint ('a'*40) -CertificatePath fixture.pfx }
    Rejected { & $entry -UseCertificateStore }
    $tokens=$null; $errors=$null
    $ast=[Management.Automation.Language.Parser]::ParseFile($entry,[ref]$tokens,[ref]$errors)
    Check (!$errors) 'Product packaging entry point parses.'
    $format=@($ast.ParamBlock.Parameters | Where-Object { $_.Name.VariablePath.UserPath -eq 'Format' })[0]
    Check ($format.DefaultValue.Value -eq 'Msi') 'MSI remains default.'
    & "$tools/signing/new-dev-certificate.ps1" -Subject 'CN=Agenda.Development' -PublicCertificatePath "$fixture/whatif/unused.cer" -WhatIf | Out-Null
    Check (!(Test-Path "$fixture/whatif")) 'Shared certificate WhatIf creates no files.'
    Write-Host 'Agenda packaging delegation checks passed.'
} finally {
    $target=[IO.Path]::GetFullPath($fixture)
    if([IO.Path]::GetDirectoryName($target) -ne [IO.Path]::GetTempPath().TrimEnd('\') -or [IO.Path]::GetFileName($target) -notlike 'AgendaPackaging-*') { throw 'Unsafe packaging test cleanup.' }
    Remove-Item -LiteralPath $target -Recurse -Force
}
