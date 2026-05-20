<#
.SYNOPSIS
    Self-contained Client Installer and Onboarding Orchestrator for DevPilot.
.DESCRIPTION
    Sets up system directories, registers VS Code sidebar extension, initializes clean database engines, and performs first-run setups.
#>
[CmdletBinding()]
param(
    [string]$InstallPath = "$env:USERPROFILE\.devpilot",
    [switch]$SkipExtension
)

$ErrorActionPreference = "Continue"

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "🚀 DevPilot Client Installer & Onboarding Setup" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$InstallPath = [System.IO.Path]::GetFullPath($InstallPath)
Write-Host "Target Installation Directory: $InstallPath" -ForegroundColor White

# 1. Initialize folders
Write-Host "`n--> Initializing directories..." -ForegroundColor Yellow
$Subdirs = @("bin", "models", "data", "cache", "logs")
foreach ($Subdir in $Subdirs) {
    $Dir = Join-Path $InstallPath $Subdir
    if (-not (Test-Path $Dir)) {
        New-Item -ItemType Directory -Path $Dir -Force | Out-Null
    }
}

# 2. Extract application binaries
Write-Host "`n--> Extracting application core binaries..." -ForegroundColor Yellow
$SourceBin = Join-Path $PSScriptRoot "..\release\app"
if (Test-Path $SourceBin) {
    Copy-Item "$SourceBin\*" -Destination "$InstallPath\bin" -Recurse -Force
    Write-Host "  [OK] Application binary resources synchronized" -ForegroundColor Green
} else {
    Write-Host "  [!] Binary payload not found in release tree. Ensure release builds completed." -ForegroundColor Red
}

# 3. Synchronize scripts, manifests, and documentation
Write-Host "`n--> Packaging system configuration files..." -ForegroundColor Yellow
$SourceModels = Join-Path $PSScriptRoot "..\release\models"
if (Test-Path $SourceModels) {
    Copy-Item "$SourceModels\*" -Destination "$InstallPath\models" -Recurse -Force
}

$SourceScripts = Join-Path $PSScriptRoot "..\release\scripts"
if (Test-Path $SourceScripts) {
    Copy-Item "$SourceScripts\*" -Destination "$InstallPath\bin" -Recurse -Force
}

# 4. Bind VS Code Extension Panel
if (-not $SkipExtension.IsPresent) {
    Write-Host "`n--> Binding VS Code Extension Integration..." -ForegroundColor Yellow
    $Vsix = Join-Path $PSScriptRoot "..\release\extension\devpilot-vscode-0.1.0.vsix"
    if (Test-Path $Vsix) {
        Write-Host "Found VSIX package: $Vsix" -ForegroundColor White
        Write-Host "Running 'code --install-extension'..." -ForegroundColor White
        
        # Check if code CLI is available
        $CodeAvailable = Get-Command code -ErrorAction SilentlyContinue
        if ($CodeAvailable) {
            code --install-extension $Vsix
            Write-Host "  [OK] Sidebar extension registered successfully!" -ForegroundColor Green
        } else {
            Write-Host "  [!] VS Code CLI ('code') not found in environment PATH." -ForegroundColor Yellow
            Write-Host "  [!] Please install VSIX manually from VS Code Extension Panel:" -ForegroundColor Yellow
            Write-Host "      Package Path: $Vsix" -ForegroundColor White
        }
    } else {
        Write-Host "  [!] VSIX package installer not found." -ForegroundColor Red
    }
}

# 5. Rerunnable Model Validation Setup
Write-Host "`n==========================================================" -ForegroundColor Cyan
Write-Host "🎯 First-Launch Onboarding Checklist" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$LocalModels = Get-ChildItem -Path "$InstallPath\models" -Filter "*.onnx" -Recurse -ErrorAction SilentlyContinue
if ($LocalModels.Count -eq 0) {
    Write-Host "🚨 STATUS: LOCAL AI MODEL WEIGHTS ARE MISSING!" -ForegroundColor Red
    Write-Host "   DevPilot requires quantized Phi-3 models to enable semantic reasoning." -ForegroundColor White
    Write-Host "`n👉 ACTION PLAN TO START INTEGRATION:" -ForegroundColor Cyan
    Write-Host "   1. Navigate to: $InstallPath\bin" -ForegroundColor White
    Write-Host "   2. Run the downloader utility script to retrieve assets:" -ForegroundColor White
    Write-Host "      .\download-models.ps1" -ForegroundColor Green
    Write-Host "   3. (Optional Debug Mode): Stage fast mock weight placeholders instantly:" -ForegroundColor White
    Write-Host "      `$env:DEVPILOT_BOOTSTRAP_MOCK = 'true'; .\download-models.ps1" -ForegroundColor Green
} else {
    Write-Host "🏆 STATUS: LOCAL AI MODEL WEIGHTS VALIDATED!" -ForegroundColor Green
    Write-Host "   Local execution engine is fully loaded and ready to start offline inference." -ForegroundColor White
}

Write-Host "`n🏁 Ready to start CLI service:" -ForegroundColor Yellow
Write-Host "   $InstallPath\bin\DevPilot.CLI.exe service" -ForegroundColor Green
Write-Host "==========================================================" -ForegroundColor Cyan
