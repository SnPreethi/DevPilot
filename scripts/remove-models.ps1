# remove-models.ps1
# Cleanly purges all downloaded local model weight binaries and tokenizer files from the repository workspace.

$ErrorActionPreference = "Stop"

Write-Host ">>> STARTING DEVPILOT MODEL PURGE UTILITY <<<" -ForegroundColor Red

# Resolve paths
$scriptPath = Split-Path -Parent $MyInvocation.MyCommand.Definition
$rootPath = Resolve-Path (Join-Path $scriptPath "..")
$devPilotPath = Join-Path $rootPath "DevPilot"
$manifestPath = Join-Path $devPilotPath "models\model-manifest.json"

if (!(Test-Path $manifestPath)) {
    Write-Host "MANIFEST CRITICAL ERROR: Manifest file not found. Purge aborted." -ForegroundColor Red
    exit 1
}

$manifest = Get-Content -Raw -Path $manifestPath | ConvertFrom-Json
$models = $manifest.Models

Write-Host "Processing file removal routine..." -ForegroundColor Gray

foreach ($model in $models) {
    foreach ($file in $model.Files) {
        $filePath = Join-Path $devPilotPath "models\$($file.TargetPath)"
        if (Test-Path $filePath) {
            Remove-Item -Path $filePath -Force
            Write-Host "    [REMOVED] Deleted local asset: DevPilot/models/$($file.TargetPath)" -ForegroundColor Yellow
        }
    }
}

# Clean up empty subdirectories under models/ but preserve .gitkeep files
Write-Host "Cleaning empty directories under models..." -ForegroundColor Gray
$subDirs = Get-ChildItem -Path (Join-Path $devPilotPath "models") -Directory -Recurse | Sort-Object -Property FullName -Descending

foreach ($subDir in $subDirs) {
    $files = Get-ChildItem -Path $subDir.FullName -Force
    # If the folder has no files or only has .gitkeep, we keep it but clean up other empty nodes
    $nonKeepFiles = $files | Where-Object { $_.Name -ne ".gitkeep" }
    if ($nonKeepFiles.Count -eq 0) {
        # Check if the folder itself has any children directories
        $subSubDirs = Get-ChildItem -Path $subDir.FullName -Directory
        if ($subSubDirs.Count -eq 0) {
            # Safely keep directory if it has .gitkeep directly, otherwise we can delete empty ones
            $hasKeep = Test-Path (Join-Path $subDir.FullName ".gitkeep")
            if (!$hasKeep) {
                Remove-Item -Path $subDir.FullName -Force -Recurse
                Write-Host "    [CLEANED] Removed empty folder: DevPilot/models/$($subDir.Name)" -ForegroundColor Yellow
            }
        }
    }
}

Write-Host "------------------------------------------------------------" -ForegroundColor Cyan
Write-Host "SUCCESS: Purge completed. Workspace models directory is reset to placeholders!" -ForegroundColor Green
