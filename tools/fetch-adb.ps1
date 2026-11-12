param([string]$ProxyUrl)
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
$vendor=Join-Path $repo 'vendor'
$zip=Join-Path $vendor 'platform-tools_r37.0.1-win.zip'
$expected='45f4d63113e895ebde0c90f194099a4676b6ac653bd28d54314a9e022bbc1a99'
if(!(Test-Path -LiteralPath $zip)){
    if(!$ProxyUrl){
        $settings=Get-ItemProperty -LiteralPath 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Internet Settings'
        if($settings.ProxyEnable -eq 1){
            $server=[string]$settings.ProxyServer
            if($server.Contains('=')){ $item=$server.Split(';') | Where-Object {$_ -like 'https=*'} | Select-Object -First 1; if($item){$server=$item.Substring(6)} else {$server=''} }
            if($server){$ProxyUrl=if($server.Contains('://')){$server}else{'http://'+$server}}
        }
    }
    if(!$ProxyUrl){throw 'A proxy is required for downloading. Supply -ProxyUrl http://host:port, or place the pinned official ZIP in vendor first.'}
    New-Item -ItemType Directory -Path $vendor -ErrorAction SilentlyContinue | Out-Null
    $download=Join-Path $vendor ('download-'+[guid]::NewGuid().ToString('N')+'.zip')
    try {
        Invoke-WebRequest -UseBasicParsing -Proxy $ProxyUrl -Uri 'https://dl.google.com/android/repository/platform-tools_r37.0.1-win.zip' -OutFile $download
        if((Get-FileHash -LiteralPath $download -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expected){throw 'ADB archive hash mismatch'}
        Move-Item -LiteralPath $download -Destination $zip
    } finally {if(Test-Path -LiteralPath $download){Remove-Item -LiteralPath $download}}
}
if((Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expected){throw 'ADB archive hash mismatch'}
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive=[IO.Compression.ZipFile]::OpenRead($zip)
$destination=Join-Path $vendor 'adb-37.0.1'
New-Item -ItemType Directory -Path $destination -ErrorAction SilentlyContinue | Out-Null
try {
    foreach($name in @('adb.exe','AdbWinApi.dll','AdbWinUsbApi.dll','NOTICE.txt')){
        $entry=$archive.GetEntry('platform-tools/'+$name)
        if(!$entry){throw "ADB archive is missing $name"}
        $entryStream=$entry.Open()
        $targetStream=[IO.File]::Create((Join-Path $destination $name))
        try {$entryStream.CopyTo($targetStream)} finally {$targetStream.Dispose();$entryStream.Dispose()}
    }
} finally {$archive.Dispose()}
Write-Host "Verified official ADB 37.0.1 extracted: $destination"
