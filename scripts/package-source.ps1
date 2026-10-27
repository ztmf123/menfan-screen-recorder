. "$PSScriptRoot/common.ps1"
[xml]$project = Get-Content (Join-Path $ProjectRoot 'src/Menfan.App/Menfan.App.csproj') -Raw
$releaseVersion = [string]$project.Project.PropertyGroup.Version
$stage = Join-Path $ProjectRoot "artifacts/source-v$releaseVersion"
New-Item -ItemType Directory -Path $stage -Force | Out-Null
$sourceFiles = foreach ($folder in @('.github', 'docs', 'licenses', 'scripts', 'src', 'tests')) {
    Get-ChildItem -LiteralPath (Join-Path $ProjectRoot $folder) -Recurse -File |
        Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' -and $_.Name -notmatch '^(menfan-icon-v[12]\.(png|ico)|ICON_DESIGN_V[12]\.md)$' }
}
$sourceFiles += Get-ChildItem -LiteralPath $ProjectRoot -File | Where-Object {
    $_.Name -in @('.gitignore', 'LICENSE', 'global.json', 'Menfan.ScreenRecorder.sln') -or $_.Extension -eq '.md'
}
$manifest = foreach ($file in $sourceFiles) {
    $relative = $file.FullName.Substring($ProjectRoot.Length + 1)
    $target = Join-Path $stage $relative
    New-Item -ItemType Directory -Path (Split-Path $target -Parent) -Force | Out-Null
    Copy-Item -LiteralPath $file.FullName -Destination $target -Force
    $relative
}
$manifest | Sort-Object | Set-Content (Join-Path $ProjectRoot 'artifacts/source-manifest.txt') -Encoding UTF8
$zip = Join-Path $ProjectRoot "artifacts/Menfan-Screen-Recorder-v$releaseVersion-source.zip"
# Archive exactly the selected files, so stale staging files cannot enter a release.
Add-Type -AssemblyName System.IO.Compression
$stream = [IO.File]::Create($zip)
try {
    $archive = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($relative in $manifest) {
            $entry = $archive.CreateEntry($relative.Replace('\', '/'), [IO.Compression.CompressionLevel]::Optimal)
            $inputStream = [IO.File]::OpenRead((Join-Path $stage $relative))
            $outputStream = $entry.Open()
            try { $inputStream.CopyTo($outputStream) } finally { $outputStream.Dispose(); $inputStream.Dispose() }
        }
    } finally { $archive.Dispose() }
} finally { $stream.Dispose() }
Write-Host "$zip ($($manifest.Count) files)"
