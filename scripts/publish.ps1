. "$PSScriptRoot/common.ps1"
$destination = Join-Path $ProjectRoot 'artifacts/Menfan-Screen-Recorder-win-x64'
& $Dotnet publish (Join-Path $ProjectRoot 'src/Menfan.App') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $destination
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
Copy-Item (Join-Path $ProjectRoot 'LICENSE') $destination
Copy-Item (Join-Path $ProjectRoot 'THIRD_PARTY_NOTICES.md') $destination
Copy-Item (Join-Path $ProjectRoot 'README.md') $destination
Copy-Item (Join-Path $ProjectRoot 'START_HERE.md') $destination
Copy-Item (Join-Path $ProjectRoot 'RELEASE_NOTES.md') $destination
$docsRoot = Join-Path $ProjectRoot 'docs'
foreach ($docFile in (Get-ChildItem -LiteralPath $docsRoot -Recurse -File | Where-Object { $_.Name -notmatch '^ICON_DESIGN_V[12]\.md$' })) {
    $docTarget = Join-Path (Join-Path $destination 'docs') $docFile.FullName.Substring($docsRoot.Length + 1)
    New-Item -ItemType Directory -Path (Split-Path $docTarget -Parent) -Force | Out-Null
    Copy-Item -LiteralPath $docFile.FullName -Destination $docTarget -Force
}
foreach ($retiredDraft in @('ICON_DESIGN_V1.md', 'ICON_DESIGN_V2.md')) {
    $retiredCopy = Join-Path (Join-Path $destination 'docs') $retiredDraft
    if (Test-Path -LiteralPath $retiredCopy) { Remove-Item -LiteralPath $retiredCopy }
}
Copy-Item (Join-Path $ProjectRoot 'licenses') $destination -Recurse -Force
# FFmpeg is a separately installed executable; do not redistribute it implicitly.
[xml]$project = Get-Content (Join-Path $ProjectRoot 'src/Menfan.App/Menfan.App.csproj') -Raw
$releaseVersion = [string]$project.Project.PropertyGroup.Version
$zip = Join-Path $ProjectRoot "artifacts/Menfan-Screen-Recorder-v$releaseVersion-win-x64.zip"
Compress-Archive -Path "$destination/*" -DestinationPath $zip -Force
$checksum = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText("$zip.sha256", "$checksum  $([IO.Path]::GetFileName($zip))`n")
# Keep the stable artifact name used by Windows CI and local download links.
$latest = Join-Path $ProjectRoot 'artifacts/Menfan-Screen-Recorder-win-x64.zip'
Copy-Item -LiteralPath $zip -Destination $latest -Force
[IO.File]::WriteAllText("$latest.sha256", "$checksum  $([IO.Path]::GetFileName($latest))`n")
Write-Host $zip
