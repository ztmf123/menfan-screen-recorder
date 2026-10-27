$ErrorActionPreference = 'Stop'
$script:ProjectRoot = Split-Path $PSScriptRoot -Parent
$localDotnet = Join-Path $ProjectRoot '.tools/dotnet/dotnet.exe'
$script:Dotnet = if (Test-Path $localDotnet) { $localDotnet } else { 'dotnet' }
$env:DOTNET_CLI_HOME = Join-Path $ProjectRoot '.tools/cli-home'
$env:NUGET_PACKAGES = Join-Path $ProjectRoot '.tools/nuget'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$env:TEMP = Join-Path $ProjectRoot '.tools/temp'
$env:TMP = $env:TEMP
New-Item -ItemType Directory -Force $env:TEMP | Out-Null
$ffmpeg = Join-Path $ProjectRoot '.tools/ffmpeg/bin/ffmpeg.exe'
if (Test-Path $ffmpeg) { $env:MENFAN_FFMPEG = $ffmpeg }
