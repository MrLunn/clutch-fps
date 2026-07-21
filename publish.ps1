# One command to rebuild the Windows client and republish it to the GitHub
# "latest" release, so the download link always serves the newest build:
#   https://github.com/MrLunn/clutch-fps/releases/tag/latest
#
# Usage: close the Unity editor, then run in PowerShell:  ./publish.ps1
# No CI / cloud license needed - it builds with your locally licensed editor.

$ErrorActionPreference = "Stop"

$unity   = "C:\Program Files\Unity\Hub\Editor\6000.3.20f1\Editor\Unity.exe"
$project = "C:\Users\m_lun\Projects\ClutchFPS"
$out     = "$env:USERPROFILE\Desktop\ClutchFPS-Build"
$zip     = "$env:USERPROFILE\Desktop\ClutchFPS-Windows.zip"
$repo    = "MrLunn/clutch-fps"
$gh      = "C:\Program Files\GitHub CLI\gh.exe"

if (Test-Path "$project\Temp\UnityLockfile") {
    Write-Host "Close the Unity editor first (the project is locked while it is open)." -ForegroundColor Yellow
    exit 1
}

Write-Host "Building the Windows player (a few minutes)..." -ForegroundColor Cyan
& $unity -quit -batchmode -nographics -projectPath $project -buildTarget Win64 -buildWindows64Player "$out\ClutchFPS.exe" -logFile -
if ($LASTEXITCODE -ne 0) { Write-Host "Build failed." -ForegroundColor Red; exit 1 }

# Drop the debug-symbols folder that should not ship, then zip.
$dbg = "$out\ClutchFPS_BurstDebugInformation_DoNotShip"
if (Test-Path $dbg) { Remove-Item -Recurse -Force $dbg }
if (Test-Path $zip) { Remove-Item -Force $zip }
Compress-Archive -Path $out -DestinationPath $zip

Write-Host "Uploading to the latest release..." -ForegroundColor Cyan
& $gh release upload latest "$zip#ClutchFPS-Windows.zip" --repo $repo --clobber

Write-Host "Done: https://github.com/$repo/releases/tag/latest" -ForegroundColor Green
