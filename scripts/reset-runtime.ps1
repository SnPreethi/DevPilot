# reset-runtime.ps1
# Resets operational runtime state, persistent app caches, SQLite databases, and onboarding status.

$ErrorActionPreference = "Stop"

Write-Host "======================================================================" -ForegroundColor Red
Write-Host "            DEVPILOT DESKTOP RUNTIME RESET UTILITY" -ForegroundColor Red
Write-Host "======================================================================" -ForegroundColor Red

$scriptPath = Split-Path -Parent $MyInvocation.MyCommand.Definition
$rootPath = Resolve-Path (Join-Path $scriptPath "..")
$devPilotPath = Join-Path $rootPath "DevPilot"

# 1. Reset local sqlite databases
Write-Host "--> Purging operational SQLite database stores..." -ForegroundColor Gray
$dbFiles = @("data/devpilot.db", "data/devpilot.db-wal", "data/devpilot.db-shm", ".repo-map-cache.db")
foreach ($file in $dbFiles) {
    $filePath = Join-Path $devPilotPath $file
    if (Test-Path $filePath) {
        Remove-Item -Path $filePath -Force
        Write-Host "    [RESET] Deleted local store: DevPilot/$file" -ForegroundColor Yellow
    }
}

# 2. Reset AppData persistent cache files
Write-Host "--> Purging AppData telemetry and settings indexes..." -ForegroundColor Gray
$appDataDir = Join-Path $env:APPDATA "DevPilot"
if (Test-Path $appDataDir) {
    try {
        Remove-Item -Path $appDataDir -Recurse -Force
        Write-Host "    [RESET] Cleared system cache folder: %APPDATA%/DevPilot/" -ForegroundColor Yellow
    } catch {
        Write-Host "    [WARNING] Could not clear %APPDATA%/DevPilot/ (Files locked by running background worker)" -ForegroundColor Yellow
    }
}

Write-Host "----------------------------------------------------------------------" -ForegroundColor Red
Write-Host "SUCCESS: Runtime resets applied. Product returned to factory settings." -ForegroundColor Green
