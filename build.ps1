param([string]$WinFspDir='C:\Program Files (x86)\WinFsp')
$ErrorActionPreference='Stop'
$root=$PSScriptRoot
& (Join-Path $root 'tools\fetch-winfsp.ps1')
& (Join-Path $root 'tools\fetch-adb.ps1')
$dist=Join-Path $root 'dist'
New-Item -ItemType Directory -Path $dist -ErrorAction SilentlyContinue | Out-Null
$checksums=@()
foreach($variant in @('framework-dependent','self-contained')){
    $selfContained=($variant -eq 'self-contained')
    $sc=$selfContained.ToString().ToLowerInvariant()
    $stage=Join-Path $dist ('stage-'+[guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $stage | Out-Null
    try {
        & dotnet publish (Join-Path $root 'src\AndroidAdbFs.csproj') -c Release -r win-x64 "-p:SelfContained=$sc" "-p:PublishSelfContained=$sc" -p:PublishAot=false -p:PublishTrimmed=false -p:PublishSingleFile=true -p:IncludeAllContentForSelfExtract=true "-p:EnableCompressionInSingleFile=$sc" -p:DebugType=None -p:DebugSymbols=false -o $stage "-p:WinFspDir=$WinFspDir"
        if($LASTEXITCODE -ne 0){throw "dotnet publish failed: $variant"}
        $name="AndroidAdbFs-windows-x64-$variant.exe"
        $target=Join-Path $dist $name
        Copy-Item -LiteralPath (Join-Path $stage 'AndroidAdbFs.exe') -Destination $target
        $hash=(Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash.ToLowerInvariant()
        $checksums+="$hash  $name"
        Write-Host "Single-file app: $target"
    } finally {
        $resolvedStage=[IO.Path]::GetFullPath($stage)
        if(!$resolvedStage.StartsWith([IO.Path]::GetFullPath($dist)+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Unexpected staging directory'}
        if(Test-Path -LiteralPath $resolvedStage){Remove-Item -LiteralPath $resolvedStage -Recurse}
    }
}
[IO.File]::WriteAllText((Join-Path $dist 'SHA256SUMS.txt'),($checksums -join "`n")+"`n",[Text.UTF8Encoding]::new($false))
