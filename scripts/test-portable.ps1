param([Parameter(Mandatory=$true)][string]$Directory)
$ErrorActionPreference='Stop'
$portableRoot=[IO.Path]::GetFullPath($Directory)
foreach($name in @('Agenda.App.exe','Agenda.App.pri','App.xbf','MainWindow.xbf')) {
    if(-not(Test-Path -LiteralPath (Join-Path $portableRoot $name))) { throw "Portable build is missing $name" }
}
if(Get-Process Agenda.App -ErrorAction SilentlyContinue) { throw 'Exit Agenda before running the isolated startup check.' }
$repoRoot=Split-Path $PSScriptRoot -Parent
$profile=Join-Path $repoRoot ('work/startup-'+[Guid]::NewGuid().ToString('N'))
$started=Start-Process -FilePath (Join-Path $portableRoot 'Agenda.App.exe') -ArgumentList @('--data-dir',('"'+$profile+'"')) -WorkingDirectory $portableRoot -WindowStyle Hidden -PassThru
try {
    # Startup is asynchronous; check for immediate crashes and resource-load failures.
    for($attempt=0;$attempt -lt 10;$attempt++) {
        Start-Sleep -Milliseconds 500
        $started.Refresh()
        if($started.HasExited) { throw "Portable Agenda exited during startup (code $($started.ExitCode)). Profile: $profile" }
    }
    if(Test-Path -LiteralPath (Join-Path $profile 'errors.log')) { throw "Portable Agenda logged an unhandled exception. Profile: $profile" }
    Write-Output 'PASS portable native startup: process remained running without logged exceptions.'
} finally {
    $started.Refresh()
    if(-not $started.HasExited) { Stop-Process -Id $started.Id }
}
