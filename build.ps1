param([string]$WinFspDir='C:\Program Files (x86)\WinFsp')
$ErrorActionPreference='Stop'
$root=$PSScriptRoot
$app=Join-Path $root 'app'
$dist=Join-Path $root 'dist'
New-Item -ItemType Directory -Path $app -ErrorAction SilentlyContinue | Out-Null
New-Item -ItemType Directory -Path $dist -ErrorAction SilentlyContinue | Out-Null
& dotnet publish (Join-Path $root 'src\AndroidAdbFs.csproj') -c Release --self-contained false -o $app "-p:WinFspDir=$WinFspDir"
if($LASTEXITCODE -ne 0){throw 'dotnet publish failed'}
$stage=Join-Path $dist ('stage-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path (Join-Path $stage 'app') | Out-Null
foreach($name in @('AndroidAdbFs.exe','AndroidAdbFs.dll','AndroidAdbFs.deps.json','AndroidAdbFs.runtimeconfig.json','winfsp-msil.dll')){
    Copy-Item -LiteralPath (Join-Path $app $name) -Destination (Join-Path $stage 'app')
}
foreach($name in @('start.cmd','stop.cmd','start.ps1','stop.ps1','config.json','README.md','LICENSE')){
    Copy-Item -LiteralPath (Join-Path $root $name) -Destination $stage
}
Copy-Item -LiteralPath (Join-Path $root 'third_party\WinFsp-License.txt') -Destination $stage
$zip=Join-Path $dist 'AndroidAdbFs-windows-x64.zip'
# Replace only the known build output, retaining unrelated files in dist.
if(Test-Path -LiteralPath $zip){Remove-Item -LiteralPath $zip}
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip
$resolvedStage=[IO.Path]::GetFullPath($stage)
if(!( $resolvedStage.StartsWith([IO.Path]::GetFullPath($dist)+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase))){throw 'Unexpected staging directory'}
Get-ChildItem -LiteralPath $stage -Recurse -File | Remove-Item
Get-ChildItem -LiteralPath $stage -Recurse -Directory | Sort-Object { $_.FullName.Length } -Descending | Remove-Item
Remove-Item -LiteralPath $stage
Write-Host "Package: $zip"
