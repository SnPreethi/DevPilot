# bootstrap.ps1
# Main entry point for onboarding new developers to the DevPilot codebase.

$ErrorActionPreference = "Stop"

$scriptPath = Split-Path -Parent $MyInvocation.MyCommand.Definition

Write-Host "======================================================================" -ForegroundColor Cyan
Write-Host "            DEVPILOT REPOSITORY BOOTSTRAP SYSTEM" -ForegroundColor Cyan
Write-Host "======================================================================" -ForegroundColor Cyan

# ------------------------------------------------------------------------------
# Step 1: Validate system prerequisites
# ------------------------------------------------------------------------------
Write-Host "--> Step 1: Validating system pre-requisites..." -ForegroundColor Gray
$valScript = Join-Path $scriptPath "validate-prerequisites.ps1"
try {
    & powershell -NoProfile -ExecutionPolicy Bypass -File $valScript
} catch {
    Write-Host "ERROR: System pre-requisite validation failed. Bootstrapping aborted." -ForegroundColor Red
    exit 1
}

# ------------------------------------------------------------------------------
# Step 2: Ensure huggingface-cli is available
# Required for Phi-3 ONNX model downloads (Hugging Face Xet storage).
# Skipped gracefully if Python is not installed - a warning is shown instead.
# ------------------------------------------------------------------------------
Write-Host ""
Write-Host "--> Step 2: Checking huggingface-cli for model downloads..." -ForegroundColor Gray
Write-Host "======================================================================" -ForegroundColor Cyan
Write-Host "            DEVPILOT HUGGING FACE CLI CHECK" -ForegroundColor Cyan
Write-Host "======================================================================" -ForegroundColor Cyan

$hfAvailable = $false

# Resolve python executable (handles both "python" and "python3" on PATH)
$pythonExe = $null
foreach ($candidate in @("python", "python3")) {
    $cmd = Get-Command $candidate -ErrorAction SilentlyContinue
    if ($cmd) { $pythonExe = $cmd.Source; break }
}

if (-not $pythonExe) {
    Write-Host "[ WARN ] Python not found on PATH." -ForegroundColor Yellow
    Write-Host "         huggingface-cli cannot be installed automatically." -ForegroundColor Yellow
    Write-Host "         Phi-3 ONNX models will NOT download without it." -ForegroundColor Yellow
    Write-Host "         Install Python 3.9+ from https://python.org" -ForegroundColor Yellow
    Write-Host "         then re-run bootstrap.ps1, or run manually:" -ForegroundColor Yellow
    Write-Host "             pip install huggingface_hub[cli]" -ForegroundColor Yellow
} else {
    # Check if already installed
    $hfCmd = Get-Command "huggingface-cli" -ErrorAction SilentlyContinue
    if ($hfCmd) {
        $hfVer = (huggingface-cli --version 2>&1) | Select-Object -First 1
        Write-Host "[ PASS ] huggingface-cli found: $hfVer" -ForegroundColor Green
        $hfAvailable = $true
    } else {
        Write-Host "[ INFO ] huggingface-cli not found - installing via pip..." -ForegroundColor Cyan
        try {
            & $pythonExe -m pip install --quiet "huggingface_hub[cli]"

            # Refresh PATH in this session so the newly installed script is visible
            $env:PATH = [System.Environment]::GetEnvironmentVariable("PATH","Machine") + ";" + [System.Environment]::GetEnvironmentVariable("PATH","User")

            $hfCmd = Get-Command "huggingface-cli" -ErrorAction SilentlyContinue
            if ($hfCmd) {
                $hfVer = (huggingface-cli --version 2>&1) | Select-Object -First 1
                Write-Host "[ PASS ] huggingface-cli installed: $hfVer" -ForegroundColor Green
                $hfAvailable = $true
            } else {
                Write-Host "[ WARN ] huggingface-cli installed but not yet on PATH." -ForegroundColor Yellow
                Write-Host "         Close and reopen this terminal, then re-run bootstrap.ps1." -ForegroundColor Yellow
            }
        } catch {
            Write-Host "[ WARN ] Auto-install failed: $_" -ForegroundColor Yellow
            Write-Host "         Run manually: pip install huggingface_hub[cli]" -ForegroundColor Yellow
        }
    }
}

Write-Host "----------------------------------------------------------------------"
if ($hfAvailable) {
    Write-Host "SUCCESS: huggingface-cli is ready." -ForegroundColor Green
} else {
    Write-Host "WARNING: huggingface-cli unavailable. Model downloads will fail." -ForegroundColor Yellow
    Write-Host "         You can still proceed; fix this before running download-models.ps1." -ForegroundColor Yellow
}

# ------------------------------------------------------------------------------
# Step 3: Configure local dev environment
# ------------------------------------------------------------------------------
Write-Host ""
Write-Host "--> Step 3: Configuring local dev environment..." -ForegroundColor Gray
$setupScript = Join-Path $scriptPath "setup-dev-env.ps1"
try {
    & powershell -NoProfile -ExecutionPolicy Bypass -File $setupScript
} catch {
    Write-Host "ERROR: Environment setup failed. Bootstrapping aborted." -ForegroundColor Red
    exit 1
}

# ------------------------------------------------------------------------------
# Onboarding guidance
# ------------------------------------------------------------------------------
Write-Host ""
Write-Host "======================================================================" -ForegroundColor Green
Write-Host "            DEVPILOT DEV-ENVIRONMENT READY TO RUN!" -ForegroundColor Green
Write-Host "======================================================================" -ForegroundColor Green
Write-Host "Congratulations! Your system is successfully bootstrapped."
Write-Host ""
Write-Host "Here is how to start developing and testing DevPilot:"
Write-Host ""
Write-Host "  1. Download AI Models (run once after cloning):" -ForegroundColor White
Write-Host "     .\scripts\download-models.ps1 -Variant cpu" -ForegroundColor Yellow
Write-Host "     .\scripts\download-models.ps1 -Variant cuda       # NVIDIA GPU" -ForegroundColor Yellow
Write-Host "     .\scripts\download-models.ps1 -Variant directml   # any DirectX 12 GPU" -ForegroundColor Yellow
Write-Host ""
Write-Host "  2. Start the Local API Service (port 5071):" -ForegroundColor White
Write-Host "     dotnet run --project DevPilot/src/DevPilot.CLI/DevPilot.CLI.csproj service" -ForegroundColor Yellow
Write-Host ""
Write-Host "  3. Test the VS Code Sidebar Extension:" -ForegroundColor White
Write-Host "     - Open the 'DevPilot.VSCodeExtension' folder in VS Code." -ForegroundColor Gray
Write-Host "     - Press 'F5' to launch a sandboxed Extension Host." -ForegroundColor Gray
Write-Host "     - Click the glowing DevPilot icon in the left Activity Bar." -ForegroundColor Gray
Write-Host ""
Write-Host "  4. Run Automated Unit Tests:" -ForegroundColor White
Write-Host "     dotnet test DevPilot" -ForegroundColor Yellow
Write-Host "======================================================================" -ForegroundColor Green