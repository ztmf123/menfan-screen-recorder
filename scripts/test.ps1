param([switch]$Desktop, [switch]$Capture)
. "$PSScriptRoot/common.ps1"
Push-Location $ProjectRoot
try {
    & $Dotnet build Menfan.ScreenRecorder.sln -c Release -m:1 -nr:false -p:UseSharedCompilation=false
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    & $Dotnet tests/Menfan.Integration/bin/Release/net8.0-windows/Menfan.Integration.dll
    if ($LASTEXITCODE -ne 0) { throw 'Integration checks failed.' }
    & $Dotnet tests/Menfan.SceneTests/bin/Release/net8.0-windows/Menfan.SceneTests.dll
    if ($LASTEXITCODE -ne 0) { throw 'Scene checks failed.' }
    if ($Desktop) {
        & $Dotnet tests/Menfan.UiSmoke/bin/Release/net8.0-windows/Menfan.UiSmoke.dll
        if ($LASTEXITCODE -ne 0) { throw 'Window lifecycle checks failed.' }
    }
    if ($Capture) {
        & $Dotnet tests/Menfan.CaptureSmoke/bin/Release/net8.0-windows/Menfan.CaptureSmoke.dll $env:MENFAN_FFMPEG --hidden-panels
        if ($LASTEXITCODE -ne 0) { throw 'Hidden-panel capture check failed.' }
        & $Dotnet tests/Menfan.CaptureSmoke/bin/Release/net8.0-windows/Menfan.CaptureSmoke.dll $env:MENFAN_FFMPEG --startup-profile --assert-responsive
        if ($LASTEXITCODE -ne 0) { throw 'Capture startup responsiveness checks failed.' }
        & $Dotnet tests/Menfan.CaptureSmoke/bin/Release/net8.0-windows/Menfan.CaptureSmoke.dll $env:MENFAN_FFMPEG --audio --pause
        if ($LASTEXITCODE -ne 0) { throw 'Desktop capture checks failed.' }
    }
} finally { Pop-Location }
