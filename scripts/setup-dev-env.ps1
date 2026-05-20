# setup-dev-env.ps1
# Restores dependencies, builds local extension bundles, and initializes clean workspaces.

$ErrorActionPreference = "Stop"

Write-Host "======================================================================" -ForegroundColor Cyan
Write-Host "            DEVPILOT DEVELOPMENT ENVIRONMENT SETUP" -ForegroundColor Cyan
Write-Host "======================================================================" -ForegroundColor Cyan

# Define relative paths based on script location
$scriptPath = Split-Path -Parent $MyInvocation.MyCommand.Definition
$rootPath = Resolve-Path (Join-Path $scriptPath "..")
$devPilotPath = Join-Path $rootPath "DevPilot"
$extensionPath = Join-Path $rootPath "DevPilot.VSCodeExtension"

# 1. Create required directories and ensure .gitkeep placeholders are present
Write-Host "--> Initializing local runtime and model directories..." -ForegroundColor Gray
$folders = @("models", "data", "cache", "logs", "runtime")
foreach ($folder in $folders) {
    $targetDir = Join-Path $devPilotPath $folder
    if (!(Test-Path $targetDir)) {
        New-Item -ItemType Directory -Path $targetDir -Force | Out-Null
    }
    $keepFile = Join-Path $targetDir ".gitkeep"
    if (!(Test-Path $keepFile)) {
        New-Item -ItemType File -Path $keepFile -Value "# Preserves $folder folder structure in version control." -Force | Out-Null
    }
    Write-Host "    [OK] Directory initialized: DevPilot/$folder/" -ForegroundColor Green
}

# 2. Restore .NET NuGet packages
Write-Host "--> Restoring .NET NuGet packages..." -ForegroundColor Gray
try {
    Push-Location $devPilotPath
    & dotnet restore
    Write-Host "    [OK] NuGet dependencies restored successfully." -ForegroundColor Green
} catch {
    Write-Error "Failed to restore NuGet packages. Ensure .NET SDK is working correctly."
} finally {
    Pop-Location
}

# 3. Restore npm dependencies for VS Code extension
Write-Host "--> Restoring Node dependencies for VS Code Extension..." -ForegroundColor Gray
try {
    Push-Location $extensionPath
    & npm install
    Write-Host "    [OK] npm packages installed successfully." -ForegroundColor Green
    
    Write-Host "--> Bundling VS Code Extension with esbuild..." -ForegroundColor Gray
    & npm run package
    Write-Host "    [OK] VS Code extension bundle generated successfully inside dist/." -ForegroundColor Green
} catch {
    Write-Error "Failed to set up VS Code extension. Check npm installations."
} finally {
    Pop-Location
}

Write-Host "----------------------------------------------------------------------" -ForegroundColor Cyan
Write-Host "SUCCESS: Developer setup completed successfully!" -ForegroundColor Green
