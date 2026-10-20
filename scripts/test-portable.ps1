param([Parameter(Mandatory=$true)][string]$Directory,[switch]$CheckNotifications)
$ErrorActionPreference='Stop'
$portableRoot=[IO.Path]::GetFullPath($Directory)
foreach($name in @('Agenda.App.exe','Agenda.App.pri','App.xbf','MainWindow.xbf')) {
    if(-not(Test-Path -LiteralPath (Join-Path $portableRoot $name))) { throw "Portable build is missing $name" }
}
if(Get-Process Agenda.App -ErrorAction SilentlyContinue) { throw 'Exit Agenda before running the isolated startup check.' }
$repoRoot=Split-Path $PSScriptRoot -Parent
$profile=Join-Path $repoRoot ('work/startup-'+[Guid]::NewGuid().ToString('N'))
$arguments=@('--data-dir',('"'+$profile+'"'))
if($CheckNotifications) { $arguments+='--notification-diagnostics' }
$started=Start-Process -FilePath (Join-Path $portableRoot 'Agenda.App.exe') -ArgumentList $arguments -WorkingDirectory $portableRoot -WindowStyle Hidden -PassThru
try {
    # Startup is asynchronous; check for immediate crashes and resource-load failures.
    for($attempt=0;$attempt -lt 10;$attempt++) {
        Start-Sleep -Milliseconds 500
        $started.Refresh()
        if($started.HasExited) { throw "Portable Agenda exited during startup (code $($started.ExitCode)). Profile: $profile" }
    }
    if(Test-Path -LiteralPath (Join-Path $profile 'errors.log')) { throw "Portable Agenda logged an unhandled exception. Profile: $profile" }
    if($CheckNotifications) {
        $diagnostics=Join-Path $profile 'notifications.log'
        if(!(Test-Path -LiteralPath $diagnostics) -or (Get-Content -LiteralPath $diagnostics -Raw) -notmatch 'Registered; Windows setting:') {
            throw "Portable notification registration did not complete. Profile: $profile"
        }
        Write-Output 'PASS unpackaged notification registration (delivery/activation not exercised).'
    }
    Write-Output 'PASS portable native startup: process remained running without logged exceptions.'
} finally {
    $started.Refresh()
    if(-not $started.HasExited) { Stop-Process -Id $started.Id }
}
