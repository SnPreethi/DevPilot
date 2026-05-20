# download-models.ps1
# Automates the downloading and placement of local ONNX models based on the model manifest.

param (
    [string]$TargetModelId = "", 
    [switch]$ForceDownload = $false
)

$ErrorActionPreference = "Stop"

Write-Host "======================================================================" -ForegroundColor Cyan
Write-Host "            DEVPILOT LOCAL AI MODEL DOWNLOAD SYSTEM" -ForegroundColor Cyan
Write-Host "======================================================================" -ForegroundColor Cyan

# Resolve manifest path
$scriptPath = Split-Path -Parent $MyInvocation.MyCommand.Definition
$rootPath = Resolve-Path (Join-Path $scriptPath "..")
$devPilotPath = Join-Path $rootPath "DevPilot"
$manifestPath = Join-Path $devPilotPath "models\model-manifest.json"

if (!(Test-Path $manifestPath)) {
    Write-Error "Model manifest was not found at: $manifestPath"
    exit 1
}

# Parse manifest
$manifest = Get-Content -Raw -Path $manifestPath | ConvertFrom-Json
$availableModels = $manifest.Models

# Model selection logic
$selectedModels = @()

if ($TargetModelId) {
    $found = $availableModels | Where-Object { $_.Id -eq $TargetModelId }
    if ($found) {
        $selectedModels += $found
    } else {
        Write-Host "ERROR: Model ID '$TargetModelId' not found in manifest!" -ForegroundColor Red
        Write-Host "Available IDs: $( ($availableModels.Id) -join ', ' )" -ForegroundColor Yellow
        exit 1
    }
} else {
    $selectedModels = $availableModels
}

# Download execution routine
foreach ($model in $selectedModels) {
    Write-Host ""
    Write-Host "----------------------------------------------------------------------" -ForegroundColor Gray
    Write-Host "Processing: $($model.Name)" -ForegroundColor White
    Write-Host "Description: $($model.Description)" -ForegroundColor Gray
    
    foreach ($file in $model.Files) {
        $destPath = Join-Path $devPilotPath "models\$($file.TargetPath)"
        $destFolder = Split-Path -Parent $destPath
        
        # Ensure destination folder exists
        if (!(Test-Path $destFolder)) {
            New-Item -ItemType Directory -Path $destFolder -Force | Out-Null
        }
        
        # Check if file exists and matches size
        if (Test-Path $destPath) {
            $existingSize = (Get-Item $destPath).Length
            if ($existingSize -eq $file.SizeBytes -and !$ForceDownload) {
                Write-Host "    [SKIPPED] Already downloaded: $($file.TargetPath) (Size matches: $( [Math]::Round($existingSize/1MB, 2) ) MB)" -ForegroundColor Green
                continue
            }
        }
        
        Write-Host "    --> Downloading: $($file.Url)" -ForegroundColor Gray
        Write-Host "        Target Destination: DevPilot/models/$($file.TargetPath)" -ForegroundColor Gray
        Write-Host "        File size: $( [Math]::Round($file.SizeBytes/1MB, 2) ) MB" -ForegroundColor Gray
        
        try {
            # Fast test-mode switch: writes mock weights instantly if DEVPILOT_BOOTSTRAP_MOCK is active
            if ($env:DEVPILOT_BOOTSTRAP_MOCK -eq "true") {
                [System.IO.File]::WriteAllText($destPath, "MOCK MODEL WEIGHTS FOR $($file.TargetPath)")
                Write-Host "    [OK] Mock generated for: $($file.TargetPath)" -ForegroundColor Yellow
            } else {
                # Real download via native WebClient
                $progressPreference = 'SilentlyContinue'
                $webClient = New-Object System.Net.WebClient
                $webClient.DownloadFile($file.Url, $destPath)
                Write-Host "    [OK] Downloaded: $($file.TargetPath)" -ForegroundColor Green
            }
        } catch {
            Write-Host "    [ERROR] Download failed for URL: $($file.Url)" -ForegroundColor Red
            Write-Host "    Exception: $_" -ForegroundColor Red
        }
    }
}

Write-Host "----------------------------------------------------------------------" -ForegroundColor Cyan
Write-Host "Model provisioning process finalized." -ForegroundColor Green
