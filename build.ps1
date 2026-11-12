param([string]$WinFspDir='C:\Program Files (x86)\WinFsp')
$ErrorActionPreference='Stop'
$root=$PSScriptRoot
$dist=Join-Path $root 'dist'
New-Item -ItemType Directory -Path $dist -ErrorAction SilentlyContinue | Out-Null
$checksums=@()
foreach($variant in @('framework-dependent','self-contained')){
    $selfContained=($variant -eq 'self-contained')
    $stage=Join-Path $dist ('stage-'+[guid]::NewGuid().ToString('N'))
    $app=Join-Path $stage 'app'
    New-Item -ItemType Directory -Path $app | Out-Null
    try {
        & dotnet publish (Join-Path $root 'src\AndroidAdbFs.csproj') -c Release -r win-x64 --self-contained $selfContained.ToString().ToLowerInvariant() -p:PublishAot=false -p:PublishTrimmed=false -o $app "-p:WinFspDir=$WinFspDir"
        if($LASTEXITCODE -ne 0){throw "dotnet publish failed: $variant"}
        Get-ChildItem -LiteralPath $app -File | Where-Object {$_.Extension -in @('.pdb','.xml')} | Remove-Item
        foreach($name in @('start.cmd','stop.cmd','start.ps1','stop.ps1','config.json','README.md','LICENSE')){
            Copy-Item -LiteralPath (Join-Path $root $name) -Destination $stage
        }
        Copy-Item -LiteralPath (Join-Path $root 'third_party\WinFsp-License.txt') -Destination $stage
        [IO.File]::WriteAllText((Join-Path $stage 'PACKAGE.txt'),"Variant: $variant`r`nRequires installed .NET 10 x64 Runtime: $(!$selfContained)`r`nBoth variants require ADB and WinFsp.`r`n",[Text.UTF8Encoding]::new($false))
        $name="AndroidAdbFs-windows-x64-$variant.zip"
        $zip=Join-Path $dist $name
        if(Test-Path -LiteralPath $zip){Remove-Item -LiteralPath $zip}
        Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip
        $hash=(Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
        $checksums+="$hash  $name"
        Write-Host "Package: $zip"
    } finally {
        $resolvedStage=[IO.Path]::GetFullPath($stage)
        if(!$resolvedStage.StartsWith([IO.Path]::GetFullPath($dist)+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Unexpected staging directory'}
        if(Test-Path -LiteralPath $resolvedStage){Remove-Item -LiteralPath $resolvedStage -Recurse}
    }
}
[IO.File]::WriteAllText((Join-Path $dist 'SHA256SUMS.txt'),($checksums -join "`n")+"`n",[Text.UTF8Encoding]::new($false))
