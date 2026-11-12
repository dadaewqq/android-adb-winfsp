$ErrorActionPreference='Stop'
$state=Join-Path $PSScriptRoot 'server.pid'
if(!(Test-Path -LiteralPath $state)){Write-Host 'No mount recorded.';exit}
$mountPid=[int](Get-Content -LiteralPath $state)
$process=Get-Process -Id $mountPid -ErrorAction SilentlyContinue
if(!$process){Write-Host 'Mount process already exited.';exit}
if($process.Path -ne (Join-Path $PSScriptRoot 'app\AndroidAdbFs.exe')){throw 'PID belongs to another process; no action taken.'}
[IO.File]::WriteAllText((Join-Path $PSScriptRoot 'stop.signal'),'stop')
if(!$process.WaitForExit(15000)){throw 'Waiting for file operations. Close programs using the drive and run stop.cmd again.'}
Remove-Item -LiteralPath $state
Write-Host 'Unmounted.'
