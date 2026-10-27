. "$PSScriptRoot/common.ps1"
Push-Location $ProjectRoot
try {
    & $Dotnet run --project src/Menfan.App -c Release
    if ($LASTEXITCODE -ne 0) { throw "Application exited: $LASTEXITCODE" }
} finally { Pop-Location }
