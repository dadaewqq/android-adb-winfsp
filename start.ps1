$ErrorActionPreference='Stop'
Write-Host 'WinFsp - Windows File System Proxy, Copyright (C) Bill Zissimopoulos'
Write-Host 'https://github.com/winfsp/winfsp'
$dir=$PSScriptRoot
$config=Get-Content -LiteralPath (Join-Path $dir 'config.json') -Raw | ConvertFrom-Json
$drive=[string]$config.Drive
if($drive -notmatch '^[D-Zd-z]:$'){throw 'config.json: Drive must be a drive letter, for example K:'}
$exe=Join-Path $dir 'app\AndroidAdbFs.exe'
$stateFile=Join-Path $dir 'server.pid'
if(Test-Path -LiteralPath $stateFile){
    $running=Get-Process -Id ([int](Get-Content -LiteralPath $stateFile)) -ErrorAction SilentlyContinue
    if($running -and $running.Path -eq $exe){throw 'This copy is already running. Use stop.cmd before starting again.'}
}
if(Test-Path ($drive+'\')){throw "$drive is already occupied. Change Drive in config.json."}
$install=Get-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\WOW6432Node\WinFsp' -ErrorAction SilentlyContinue
if(!$install){$install=Get-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\WinFsp' -ErrorAction SilentlyContinue}
if(!$install -or !(Test-Path -LiteralPath (Join-Path $install.InstallDir 'bin\winfsp-x64.dll'))){throw 'Install WinFsp x64 first, then run start.cmd again.'}
$adb=[string]$config.AdbPath
if(!$adb){
    $command=Get-Command adb.exe -ErrorAction SilentlyContinue
    if($command){$adb=$command.Source}
    else {
        foreach($candidate in @("$env:ANDROID_HOME\platform-tools\adb.exe","$env:ANDROID_SDK_ROOT\platform-tools\adb.exe","$env:LOCALAPPDATA\Android\Sdk\platform-tools\adb.exe")){
            if(Test-Path -LiteralPath $candidate){$adb=$candidate;break}
        }
    }
}
if(!$adb -or !(Test-Path -LiteralPath $adb)){throw 'ADB not found. Set AdbPath in config.json to the full path of adb.exe.'}
& $adb start-server
if($LASTEXITCODE -ne 0){throw 'ADB server failed to start.'}
$stop=Join-Path $dir 'stop.signal'
if(Test-Path -LiteralPath $stop){Remove-Item -LiteralPath $stop}
# Start-Process joins arguments on Windows; quote values explicitly for paths with spaces.
function Quote([string]$value){if($value.Contains('"')){throw 'Double quotes are not allowed in config values.'};return '"'+$value+'"'}
$arguments=@('--adb',(Quote $adb),'--mount',$drive,'--root',(Quote ([string]$config.Root)),'--stop-file',(Quote $stop))
if($config.Serial){$arguments+=@('--serial',(Quote ([string]$config.Serial)))}
$p=Start-Process -FilePath $exe -ArgumentList $arguments -WindowStyle Hidden -RedirectStandardOutput (Join-Path $dir 'stdout.log') -RedirectStandardError (Join-Path $dir 'stderr.log') -PassThru
$p.Id | Set-Content -LiteralPath $stateFile
$ready=$false
for($i=0;$i -lt 100;$i++){
    if($p.HasExited){throw ('Mount failed. Check USB debugging authorization, connected devices and stderr.log: '+(Get-Content -LiteralPath (Join-Path $dir 'stderr.log') -Raw))}
    if(Test-Path ($drive+'\')){$ready=$true;break}
    Start-Sleep -Milliseconds 200
}
if(!$ready){throw 'Mount is still pending. Inspect stderr.log; run stop.cmd before retrying.'}
Write-Host "$drive mounted (read-only). Run stop.cmd to unmount."
Start-Process explorer.exe -ArgumentList ($drive+'\')
