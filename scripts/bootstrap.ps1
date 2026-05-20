# bootstrap.ps1
# Main entry point for onboarding new developers to the DevPilot codebase.

$ErrorActionPreference = "Stop"

$scriptPath = Split-Path -Parent $MyInvocation.MyCommand.Definition

Write-Host "======================================================================" -ForegroundColor Cyan
Write-Host "            DEVPILOT REPOSITORY BOOTSTRAP SYSTEM" -ForegroundColor Cyan
Write-Host "======================================================================" -ForegroundColor Cyan

# 1. Run Prerequisites Validation
Write-Host "--> Step 1: Validating system pre-requisites..." -ForegroundColor Gray
$valScript = Join-Path $scriptPath "validate-prerequisites.ps1"
try {
    & powershell -NoProfile -ExecutionPolicy Bypass -File $valScript
} catch {
    Write-Host "ERROR: System pre-requisite validation failed. Bootstrapping aborted." -ForegroundColor Red
    exit 1
}

# 2. Run Dev Environment Setup
Write-Host ""
Write-Host "--> Step 2: Configuring local dev environment..." -ForegroundColor Gray
$setupScript = Join-Path $scriptPath "setup-dev-env.ps1"
try {
    & powershell -NoProfile -ExecutionPolicy Bypass -File $setupScript
} catch {
    Write-Host "ERROR: Environment setup failed. Bootstrapping aborted." -ForegroundColor Red
    exit 1
}

# 3. Print Onboarding Guidance
Write-Host ""
Write-Host "======================================================================" -ForegroundColor Green
Write-Host "            DEVPILOT DEV-ENVIRONMENT READY TO RUN!" -ForegroundColor Green
Write-Host "======================================================================" -ForegroundColor Green
Write-Host "Congratulations! Your system is successfully bootstrapped."
Write-Host ""
Write-Host "Here is how to start developing and testing DevPilot:"
Write-Host ""
Write-Host "  1. Start the Local API Service (Runs background task service on port 5071):" -ForegroundColor White
Write-Host "     dotnet run --project DevPilot/src/DevPilot.CLI/DevPilot.CLI.csproj service" -ForegroundColor Yellow
Write-Host ""
Write-Host "  2. Test the VS Code Sidebar Extension:" -ForegroundColor White
Write-Host "     - Open the 'DevPilot.VSCodeExtension' folder in VS Code." -ForegroundColor Gray
Write-Host "     - Press 'F5' to launch a sandboxed Extension Host." -ForegroundColor Gray
Write-Host "     - Click the glowing DevPilot icon in the left Activity Bar." -ForegroundColor Gray
Write-Host ""
Write-Host "  3. Run Automated Unit Tests:" -ForegroundColor White
Write-Host "     dotnet test DevPilot" -ForegroundColor Yellow
Write-Host ""
Write-Host "  4. Model Setup:" -ForegroundColor White
Write-Host "     Open the DevPilot extension in VS Code. The UI will detect missing models" -ForegroundColor Gray
Write-Host "     and prompt you to automatically download models offline with one click." -ForegroundColor Gray
Write-Host "======================================================================" -ForegroundColor Green
