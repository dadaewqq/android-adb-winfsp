param([string]$ProxyUrl)
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
$vendor=Join-Path $repo 'vendor'
$path=Join-Path $vendor 'winfsp-2.1.25156.msi'
$expected='073a70e00f77423e34bed98b86e600def93393ba5822204fac57a29324db9f7a'
if(!(Test-Path -LiteralPath $path)){
    if(!$ProxyUrl){
        $settings=Get-ItemProperty -LiteralPath 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Internet Settings'
        if($settings.ProxyEnable -eq 1){
            $server=[string]$settings.ProxyServer
            if($server.Contains('=')){ $item=$server.Split(';') | Where-Object {$_ -like 'https=*'} | Select-Object -First 1; if($item){$server=$item.Substring(6)} else {$server=''} }
            if($server){$ProxyUrl=if($server.Contains('://')){$server}else{'http://'+$server}}
        }
    }
    if(!$ProxyUrl){throw 'A proxy is required for downloading. Supply -ProxyUrl http://host:port, or place the pinned official MSI in vendor first.'}
    New-Item -ItemType Directory -Path $vendor -ErrorAction SilentlyContinue | Out-Null
    $download=Join-Path $vendor ('download-'+[guid]::NewGuid().ToString('N')+'.msi')
    try {
        Invoke-WebRequest -UseBasicParsing -Proxy $ProxyUrl -Uri 'https://github.com/winfsp/winfsp/releases/download/v2.1/winfsp-2.1.25156.msi' -OutFile $download
        if((Get-FileHash -LiteralPath $download -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expected){throw 'WinFsp MSI hash mismatch'}
        Move-Item -LiteralPath $download -Destination $path
    } finally {if(Test-Path -LiteralPath $download){Remove-Item -LiteralPath $download}}
}
if((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expected){throw 'WinFsp MSI hash mismatch'}
$signature=Get-AuthenticodeSignature -LiteralPath $path
if($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'CN=NAVIMATICS LLC(?:,|$)'){throw 'WinFsp MSI signature validation failed'}
Write-Host "Verified official WinFsp installer: $path"
