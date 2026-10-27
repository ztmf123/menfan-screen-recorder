param([switch]$SkipSdk)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$toolRoot = Join-Path $root '.tools'
New-Item -ItemType Directory -Force $toolRoot | Out-Null
if (-not $SkipSdk -and -not (Test-Path (Join-Path $toolRoot 'dotnet/dotnet.exe'))) {
    $installer = Join-Path $toolRoot 'dotnet-install.ps1'
    Invoke-WebRequest https://dot.net/v1/dotnet-install.ps1 -OutFile $installer
    & $installer -Channel 8.0 -InstallDir (Join-Path $toolRoot 'dotnet') -NoPath
    if (-not (Test-Path (Join-Path $toolRoot 'dotnet/dotnet.exe'))) { throw 'SDK installation failed.' }
}
$ffmpeg = Join-Path $toolRoot 'ffmpeg/bin/ffmpeg.exe'
if (-not (Test-Path $ffmpeg)) {
    $archive = Join-Path $toolRoot 'ffmpeg-essentials.zip'
    $hashFile = Join-Path $toolRoot 'ffmpeg-essentials.sha256'
    $url = 'https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip'
    Invoke-WebRequest $url -OutFile $archive
    Invoke-WebRequest "$url.sha256" -OutFile $hashFile
    $match = [regex]::Match((Get-Content $hashFile -Raw), '(?i)\b[0-9a-f]{64}\b')
    if (-not $match.Success -or (Get-FileHash $archive -Algorithm SHA256).Hash -ne $match.Value) { throw 'FFmpeg checksum verification failed.' }
    $stage = Join-Path $toolRoot ('ffmpeg-stage-' + [guid]::NewGuid().ToString('N'))
    Expand-Archive -LiteralPath $archive -DestinationPath $stage
    $binary = Get-ChildItem -LiteralPath $stage -Filter ffmpeg.exe -Recurse | Select-Object -First 1
    if (-not $binary) { throw 'ffmpeg.exe not found in archive.' }
    $distribution = Split-Path (Split-Path $binary.FullName -Parent) -Parent
    Copy-Item -LiteralPath $distribution -Destination (Join-Path $toolRoot 'ffmpeg') -Recurse
}
Write-Host 'Ready. Start with: .\scripts\run.ps1' -ForegroundColor Green
