# bootstrap.ps1
# Main entry point for onboarding new developers to the DevPilot codebase.
#
# IMPORTANT: Run this from the REPO ROOT, not from inside the scripts/ folder.
#
#   Correct:   cd C:\path\to\DevPilot-repo
#              .\scripts\bootstrap.ps1
#
#   Incorrect: cd C:\path\to\DevPilot-repo\scripts
#              .\bootstrap.ps1   <-- $scriptPath resolves wrong for other scripts

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
# Step 2: Ensure the Hugging Face CLI ('hf') is available
#
# NOTE: The huggingface_hub package renamed its CLI from 'huggingface-cli'
#       to 'hf' in recent releases. 'huggingface-cli' is fully deprecated
#       and throws a NativeCommandError. All DevPilot scripts use 'hf'.
#
# This step is non-blocking: if Python is absent, a warning is shown and
# bootstrap continues so that .NET / VS Code setup still completes.
# ------------------------------------------------------------------------------
Write-Host ""
Write-Host "--> Step 2: Checking Hugging Face CLI (hf) for model downloads..." -ForegroundColor Gray
Write-Host "======================================================================" -ForegroundColor Cyan
Write-Host "            DEVPILOT HUGGING FACE CLI CHECK" -ForegroundColor Cyan
Write-Host "======================================================================" -ForegroundColor Cyan

$hfAvailable = $false

# Resolve python executable (handles both 'python' and 'python3' on PATH)
$pythonExe = $null
foreach ($candidate in @("python", "python3")) {
    $found = Get-Command $candidate -ErrorAction SilentlyContinue
    if ($found) { $pythonExe = $found.Source; break }
}

if (-not $pythonExe) {
    Write-Host "[ WARN ] Python not found on PATH." -ForegroundColor Yellow
    Write-Host "         The 'hf' CLI cannot be installed automatically." -ForegroundColor Yellow
    Write-Host "         Phi-3 ONNX models will NOT download without it." -ForegroundColor Yellow
    Write-Host "         Install Python 3.9+ from https://python.org then re-run bootstrap.ps1." -ForegroundColor Yellow
} else {
    # Check for 'hf' (current CLI name)
    $hfCmd = Get-Command "hf" -ErrorAction SilentlyContinue

    if ($hfCmd) {
        # Capture version without letting a non-zero exit code kill the script
        $hfVer = ""
        try { $hfVer = (hf --version 2>&1) | Select-Object -First 1 } catch {}
        Write-Host "[ PASS ] hf CLI found: $hfVer" -ForegroundColor Green
        $hfAvailable = $true
    } else {
        Write-Host "[ INFO ] hf CLI not found - installing via pip (huggingface_hub[cli])..." -ForegroundColor Cyan
        try {
            & $pythonExe -m pip install --quiet "huggingface_hub[cli]"

            # Refresh PATH in this session so the newly installed script is visible
            $env:PATH = [System.Environment]::GetEnvironmentVariable("PATH", "Machine") + ";" + [System.Environment]::GetEnvironmentVariable("PATH", "User")

            $hfCmd = Get-Command "hf" -ErrorAction SilentlyContinue
            if ($hfCmd) {
                $hfVer = ""
                try { $hfVer = (hf --version 2>&1) | Select-Object -First 1 } catch {}
                Write-Host "[ PASS ] hf CLI installed successfully: $hfVer" -ForegroundColor Green
                $hfAvailable = $true
            } else {
                Write-Host "[ WARN ] hf installed but not yet on PATH in this session." -ForegroundColor Yellow
                Write-Host "         Close this terminal, reopen it, then re-run bootstrap.ps1." -ForegroundColor Yellow
            }
        } catch {
            Write-Host "[ WARN ] Auto-install failed: $_" -ForegroundColor Yellow
            Write-Host "         Run manually: pip install huggingface_hub[cli]" -ForegroundColor Yellow
        }
    }
}

Write-Host "----------------------------------------------------------------------"
if ($hfAvailable) {
    Write-Host "SUCCESS: Hugging Face CLI is ready." -ForegroundColor Green
} else {
    Write-Host "WARNING: hf CLI unavailable. Model downloads will fail until resolved." -ForegroundColor Yellow
    Write-Host "         Fix: pip install huggingface_hub[cli]" -ForegroundColor Yellow
    Write-Host "         Continuing bootstrap so .NET and VS Code setup can complete..." -ForegroundColor DarkGray
}

# ------------------------------------------------------------------------------
# Step 3: Configure local dev environment (.NET restore, npm, esbuild)
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
Write-Host "     From the repo root:" -ForegroundColor DarkGray
Write-Host "     .\scripts\download-models.ps1 -Variant cpu        # CPU only (~2.3 GB)" -ForegroundColor Yellow
Write-Host "     .\scripts\download-models.ps1 -Variant cuda       # NVIDIA GPU (~7.6 GB)" -ForegroundColor Yellow
Write-Host "     .\scripts\download-models.ps1 -Variant directml   # any DirectX 12 GPU (~2.3 GB)" -ForegroundColor Yellow
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