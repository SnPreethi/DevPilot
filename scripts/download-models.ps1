#!/usr/bin/env pwsh
# ==============================================================================
#  DEVPILOT LOCAL AI MODEL DOWNLOAD SYSTEM
#  download-models.ps1
#
#  Reads model-manifest.json (SchemaVersion 2) and provisions all AI models.
#
#  Two download strategies:
#    "direct"          - Invoke-WebRequest for small CDN-hosted files (MiniLM)
#    "huggingface-cli" - huggingface-cli for large ONNX blobs (Phi-3, Xet-backed)
#
#  Usage:
#    .\scripts\download-models.ps1                     # all variants
#    .\scripts\download-models.ps1 -Variant cpu        # CPU only
#    .\scripts\download-models.ps1 -Variant cuda       # CUDA only
#    .\scripts\download-models.ps1 -Variant directml   # DirectML only
#    .\scripts\download-models.ps1 -Variant cpu,directml
#    .\scripts\download-models.ps1 -Force              # re-download even if present
# ==============================================================================

param(
    [string[]]$Variant = @("cpu", "cuda", "directml"),
    [switch]$Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# ------------------------------------------------------------------------------
# PATHS
# ------------------------------------------------------------------------------

$ScriptDir    = Split-Path -Parent $MyInvocation.MyCommand.Definition
$RepoRoot     = Split-Path $ScriptDir -Parent
$ModelsRoot   = Join-Path $RepoRoot "DevPilot/models"
$ManifestPath = Join-Path $ModelsRoot "model-manifest.json"
$TempRoot     = Join-Path $RepoRoot "DevPilot/cache/model-downloads"

# ------------------------------------------------------------------------------
# OUTPUT HELPERS
# ------------------------------------------------------------------------------

function Write-Banner($text) {
    Write-Host ""
    Write-Host ("=" * 70) -ForegroundColor Cyan
    Write-Host "            $text" -ForegroundColor Cyan
    Write-Host ("=" * 70) -ForegroundColor Cyan
}

function Write-Divider($text) {
    Write-Host ""
    Write-Host ("-" * 70) -ForegroundColor DarkGray
    if ($text) { Write-Host "  $text" -ForegroundColor White }
}

function Write-OK($msg)   { Write-Host "    [  OK  ]  $msg" -ForegroundColor Green }
function Write-Skip($msg) { Write-Host "    [ SKIP ]  $msg" -ForegroundColor DarkYellow }
function Write-Info($msg) { Write-Host "    [ INFO ]  $msg" -ForegroundColor Cyan }
function Write-Warn($msg) { Write-Host "    [ WARN ]  $msg" -ForegroundColor Yellow }
function Write-Fail($msg) { Write-Host "    [ FAIL ]  $msg" -ForegroundColor Red }

function Format-Bytes([long]$bytes) {
    if ($bytes -ge 1GB) { return "{0:N2} GB" -f ($bytes / 1GB) }
    if ($bytes -ge 1MB) { return "{0:N2} MB" -f ($bytes / 1MB) }
    return "{0:N0} bytes" -f $bytes
}

# ------------------------------------------------------------------------------
# PRE-FLIGHT: huggingface-cli
# ------------------------------------------------------------------------------

function Assert-HuggingFaceCLI {
    if (Get-Command "huggingface-cli" -ErrorAction SilentlyContinue) {
        $ver = (huggingface-cli --version 2>&1) | Select-Object -First 1
        Write-OK "huggingface-cli is available  ($ver)"
        return
    }

    Write-Warn "huggingface-cli not found - attempting pip install..."

    $pip = Get-Command "pip" -ErrorAction SilentlyContinue
    if (-not $pip) { $pip = Get-Command "pip3" -ErrorAction SilentlyContinue }

    if (-not $pip) {
        Write-Fail "pip not found. Cannot install huggingface-cli."
        Write-Fail "Install Python 3.9+ from https://python.org, then run:"
        Write-Fail "    pip install huggingface_hub[cli]"
        exit 1
    }

    try {
        & $pip.Source install --quiet "huggingface_hub[cli]"
    } catch {
        Write-Fail "pip install failed: $_"
        Write-Fail "Run manually:  pip install huggingface_hub[cli]"
        exit 1
    }

    # Refresh PATH in this session
    $env:PATH = [System.Environment]::GetEnvironmentVariable("PATH","Machine") + ";" + [System.Environment]::GetEnvironmentVariable("PATH","User")

    if (-not (Get-Command "huggingface-cli" -ErrorAction SilentlyContinue)) {
        Write-Warn "huggingface-cli installed but not yet on PATH."
        Write-Warn "Please close this terminal, reopen it, then re-run download-models.ps1"
        exit 1
    }

    Write-OK "huggingface-cli installed and ready."
}

# ------------------------------------------------------------------------------
# POST-DOWNLOAD VALIDATION
# Checks that every file listed in ExpectedFiles is present and non-empty.
# This is deliberately lightweight - validate-models.ps1 does the deep SHA check.
# ------------------------------------------------------------------------------

function Test-ExpectedFiles([string]$targetPath, [string[]]$expectedFiles) {
    if (-not $expectedFiles -or $expectedFiles.Count -eq 0) { return $true }

    $allGood = $true
    foreach ($fileName in $expectedFiles) {
        $fullPath = Join-Path $targetPath $fileName
        if (-not (Test-Path $fullPath)) {
            Write-Fail "Missing expected file: $fileName"
            $allGood = $false
        } elseif ((Get-Item $fullPath).Length -eq 0) {
            Write-Fail "Empty file (zero bytes): $fileName"
            $allGood = $false
        } else {
            $size = Format-Bytes (Get-Item $fullPath).Length
            Write-OK "Verified: $fileName  ($size)"
        }
    }
    return $allGood
}

# ------------------------------------------------------------------------------
# STRATEGY A: Direct HTTP download
# Used for small CDN-backed files (all-MiniLM vocab, model.onnx)
# ------------------------------------------------------------------------------

function Invoke-DirectDownload($file, [string]$modelsRoot) {
    $dest    = Join-Path $modelsRoot $file.TargetPath
    $destDir = Split-Path $dest -Parent

    if (-not $Force -and (Test-Path $dest)) {
        $actual = (Get-Item $dest).Length
        if ($actual -eq $file.SizeBytes) {
            Write-Skip "Already present: $($file.TargetPath)  ($(Format-Bytes $actual))"
            return $true
        }
        Write-Info "Size mismatch on $($file.TargetPath) - re-downloading."
    }

    New-Item -ItemType Directory -Force -Path $destDir | Out-Null

    Write-Info "Downloading $(Format-Bytes $file.SizeBytes): $($file.Url)"

    try {
        Invoke-WebRequest -Uri $file.Url -OutFile $dest -UseBasicParsing
        Write-OK "Downloaded: $($file.TargetPath)"
        return $true
    } catch {
        Write-Fail "Direct download failed: $($file.Url)"
        Write-Fail "  $_"
        if (Test-Path $dest) { Remove-Item $dest -Force }
        return $false
    }
}

# ------------------------------------------------------------------------------
# STRATEGY B: huggingface-cli download
# Used for large Phi-3 ONNX blobs stored on Hugging Face Xet storage.
#
# Flow:
#   1. huggingface-cli download <repo> --include "<pattern>" --local-dir <tempDir>
#      Downloads the matched files preserving the HF subfolder tree.
#   2. Copy <tempDir>/<HfSourceSubDir>/* into <modelsRoot>/<TargetPath>/
#   3. Run ExpectedFiles validation against the target path.
#   4. Clean up <tempDir>.
# ------------------------------------------------------------------------------

function Invoke-HuggingFaceCLIDownload($model, [string]$modelsRoot) {
    $targetPath = Join-Path $modelsRoot $model.TargetPath
    $tempDir    = Join-Path $TempRoot  $model.Id

    # Skip if all expected files are already present (and -Force not set)
    if (-not $Force -and (Test-Path $targetPath)) {
        $existing = Get-ChildItem $targetPath -File -Recurse
        if ($existing.Count -gt 0) {
            # Quick check: do all ExpectedFiles exist?
            $allPresent = $true
            foreach ($ef in $model.ExpectedFiles) {
                if (-not (Test-Path (Join-Path $targetPath $ef))) {
                    $allPresent = $false; break
                }
            }
            if ($allPresent) {
                Write-Skip "Already present: $($model.TargetPath)  ($($existing.Count) file(s))"
                return $true
            }
            Write-Info "Some expected files missing from $($model.TargetPath) - re-downloading."
        }
    }

    Write-Info "Repository:  $($model.HfRepo)"
    Write-Info "HF path:     $($model.HfIncludePattern)"
    Write-Info "Approx size: ~$(Format-Bytes $model.ApproximateSizeBytes)"
    Write-Info "Local path:  $($model.TargetPath)"
    Write-Host ""
    Write-Info "Starting huggingface-cli download (this will take a while for large files)..."

    # Prepare directories
    if (Test-Path $tempDir) { Remove-Item $tempDir -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $tempDir  | Out-Null
    New-Item -ItemType Directory -Force -Path $targetPath | Out-Null

    # --- Run huggingface-cli ---
    # Environment variables honoured transparently by huggingface-cli:
    #   HF_TOKEN            - for private/gated repos (set via: huggingface-cli login)
    #   HF_HUB_CACHE        - override cache location
    #   HUGGINGFACE_HUB_VERBOSITY - set to "debug" for detailed transfer logs
    try {
        Write-Host ""
        $hfExitCode = 0

        huggingface-cli download `
            $model.HfRepo `
            --include $model.HfIncludePattern `
            --local-dir $tempDir

        $hfExitCode = $LASTEXITCODE
    } catch {
        Write-Fail "huggingface-cli threw an exception: $_"
        return $false
    }

    if ($hfExitCode -ne 0) {
        Write-Fail "huggingface-cli exited with code $hfExitCode for repo: $($model.HfRepo)"
        Write-Warn "Troubleshooting:"
        Write-Warn "  - Check your internet connection to huggingface.co"
        Write-Warn "  - If behind a proxy, set HTTPS_PROXY environment variable"
        Write-Warn "  - For gated models, run: huggingface-cli login"
        Write-Warn "  - Partial downloads are cached in: $tempDir"
        return $false
    }

    # --- Copy from HF subfolder to our canonical target path ---
    $sourceSubDir = Join-Path $tempDir $model.HfSourceSubDir

    if (-not (Test-Path $sourceSubDir)) {
        Write-Fail "Expected HF subfolder not found after download: $($model.HfSourceSubDir)"
        Write-Warn "Actual temp dir contents:"
        Get-ChildItem $tempDir -Recurse | ForEach-Object {
            Write-Host "    $($_.FullName.Replace($tempDir, ''))" -ForegroundColor DarkGray
        }
        Write-Warn "This usually means the HfIncludePattern matched nothing."
        Write-Warn "Verify the path at: https://huggingface.co/$($model.HfRepo)/tree/main"
        return $false
    }

    Write-Host ""
    Write-Info "Copying model files to target..."

    $sourceFiles = Get-ChildItem $sourceSubDir -Recurse -File
    foreach ($f in $sourceFiles) {
        $rel  = $f.FullName.Substring($sourceSubDir.Length).TrimStart('\','/')
        $dest = Join-Path $targetPath $rel
        New-Item -ItemType Directory -Force -Path (Split-Path $dest -Parent) | Out-Null
        Copy-Item -Path $f.FullName -Destination $dest -Force
        Write-OK "$rel  ($(Format-Bytes $f.Length))"
    }

    # --- Validate expected files ---
    Write-Host ""
    Write-Info "Validating expected files..."
    $valid = Test-ExpectedFiles -targetPath $targetPath -expectedFiles $model.ExpectedFiles

    # --- Cleanup temp ---
    Remove-Item $tempDir -Recurse -Force -ErrorAction SilentlyContinue

    if ($sourceFiles.Count -eq 0) {
        Write-Fail "No files were copied - something went wrong with the download."
        return $false
    }

    return $valid
}

# ==============================================================================
# ENTRY POINT
# ==============================================================================

Write-Banner "DEVPILOT LOCAL AI MODEL DOWNLOAD SYSTEM"

# Load and validate manifest
if (-not (Test-Path $ManifestPath)) {
    Write-Fail "model-manifest.json not found at: $ManifestPath"
    Write-Fail "Run bootstrap.ps1 first, or verify your DevPilot/models directory."
    exit 1
}

$manifest = Get-Content $ManifestPath -Raw | ConvertFrom-Json

if ($manifest.SchemaVersion -lt 2) {
    Write-Fail "model-manifest.json is SchemaVersion $($manifest.SchemaVersion)."
    Write-Fail "This script requires SchemaVersion 2. Please update model-manifest.json."
    exit 1
}

# Check huggingface-cli only if we have entries that need it
$hfModels = $manifest.Models | Where-Object { $_.DownloadMethod -eq "huggingface-cli" }
if ($hfModels.Count -gt 0) {
    Assert-HuggingFaceCLI
}

# Ensure working directories exist
New-Item -ItemType Directory -Force -Path $ModelsRoot | Out-Null
New-Item -ItemType Directory -Force -Path $TempRoot   | Out-Null

$successCount  = 0
$failCount     = 0
$skippedCount  = 0

foreach ($model in $manifest.Models) {

    # For LLM entries, filter by the -Variant parameter
    if ($model.Category -eq "llm") {
        $wanted = $false
        foreach ($v in $Variant) {
            if ($model.Id -like "*$($v.Trim().ToLower())*") { $wanted = $true; break }
        }
        if (-not $wanted) {
            Write-Divider "Skipping: $($model.Name)"
            Write-Info "Not in requested variants: [$($Variant -join ', ')]"
            $skippedCount++
            continue
        }
    }

    Write-Divider $model.Name
    Write-Host "  Category : $($model.Category)" -ForegroundColor DarkGray
    Write-Host "  Provider : $($model.Provider)"  -ForegroundColor DarkGray
    Write-Host "  Method   : $($model.DownloadMethod)" -ForegroundColor DarkGray

    $ok = $false

    switch ($model.DownloadMethod) {
        "direct" {
            $allOk = $true
            foreach ($file in $model.Files) {
                if (-not (Invoke-DirectDownload -file $file -modelsRoot $ModelsRoot)) {
                    $allOk = $false
                }
            }
            $ok = $allOk
        }
        "huggingface-cli" {
            $ok = Invoke-HuggingFaceCLIDownload -model $model -modelsRoot $ModelsRoot
        }
        default {
            Write-Fail "Unknown DownloadMethod '$($model.DownloadMethod)' on model '$($model.Id)'"
            $ok = $false
        }
    }

    if ($ok) { $successCount++ } else { $failCount++ }
}

# ------------------------------------------------------------------------------
# SUMMARY
# ------------------------------------------------------------------------------

Write-Host ""
Write-Host ("=" * 70) -ForegroundColor Cyan
Write-Host ""

if ($failCount -eq 0) {
    Write-Host "  SUCCESS: All models provisioned." -ForegroundColor Green
    Write-Host ""
    Write-Host "  Results : $successCount downloaded, $skippedCount skipped, 0 failed" -ForegroundColor Gray
    Write-Host ""
    Write-Host "  Next step:" -ForegroundColor Cyan
    Write-Host "    .\scripts\validate-models.ps1" -ForegroundColor Yellow
} else {
    Write-Host "  PARTIAL: $successCount succeeded, $failCount failed, $skippedCount skipped." -ForegroundColor Yellow
    Write-Host ""
    Write-Host "  Troubleshooting:" -ForegroundColor Yellow
    Write-Host "    1. Verify internet access to huggingface.co" -ForegroundColor DarkGray
    Write-Host "    2. For proxy environments: set HTTPS_PROXY=http://your-proxy:port" -ForegroundColor DarkGray
    Write-Host "    3. For gated models: run  huggingface-cli login" -ForegroundColor DarkGray
    Write-Host "    4. Partial caches are in: DevPilot/cache/model-downloads/" -ForegroundColor DarkGray
    Write-Host "    5. Re-run with -Force to nuke and retry a specific variant:" -ForegroundColor DarkGray
    Write-Host "       .\scripts\download-models.ps1 -Variant cpu -Force" -ForegroundColor Yellow
}

Write-Host ""
Write-Host ("=" * 70) -ForegroundColor Cyan
Write-Host ""

if ($failCount -gt 0) { exit 1 }
