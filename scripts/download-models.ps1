#!/usr/bin/env pwsh
# ==============================================================================
#  DEVPILOT LOCAL AI MODEL DOWNLOAD SYSTEM
#  download-models.ps1
#
#  Reads model-manifest.json (SchemaVersion 2) and provisions all AI models.
#
#  Two download strategies:
#    "direct"          - Invoke-WebRequest for small CDN-hosted files (MiniLM)
#    "huggingface-cli" - 'hf' CLI for large ONNX blobs (Phi-3, Xet-backed)
#
#  NOTE: The Hugging Face CLI was renamed from 'huggingface-cli' to 'hf'.
#        This script uses 'hf' exclusively. If 'hf' is not found, run:
#            pip install huggingface_hub[cli]
#        or re-run bootstrap.ps1 which installs it automatically.
#
#  Usage (run from repo root):
#    .\scripts\download-models.ps1                     # all variants
#    .\scripts\download-models.ps1 -Variant cpu        # CPU only  (~2.3 GB)
#    .\scripts\download-models.ps1 -Variant cuda       # CUDA FP16 (~7.6 GB)
#    .\scripts\download-models.ps1 -Variant directml   # DirectML  (~2.3 GB)
#    .\scripts\download-models.ps1 -Variant cpu,directml
#    .\scripts\download-models.ps1 -Variant cpu -Force  # re-download even if present
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
# PRE-FLIGHT: verify 'hf' CLI is available
# ------------------------------------------------------------------------------

function Assert-HfCLI {
    $hfCmd = Get-Command "hf" -ErrorAction SilentlyContinue
    if ($hfCmd) {
        $ver = ""
        try { $ver = (hf --version 2>&1) | Select-Object -First 1 } catch {}
        Write-OK "hf CLI is available  ($ver)"
        return
    }

    Write-Warn "'hf' CLI not found - attempting pip install..."

    $pip = Get-Command "pip"  -ErrorAction SilentlyContinue
    if (-not $pip) { $pip = Get-Command "pip3" -ErrorAction SilentlyContinue }

    if (-not $pip) {
        Write-Fail "pip not found. Cannot install hf CLI."
        Write-Fail "Install Python 3.9+ from https://python.org then run:"
        Write-Fail "    pip install huggingface_hub[cli]"
        exit 1
    }

    try {
        & $pip.Source install --quiet "huggingface_hub[cli]"
    } catch {
        Write-Fail "pip install failed: $_"
        Write-Fail "Run manually: pip install huggingface_hub[cli]"
        exit 1
    }

    # Refresh PATH in this session
    $env:PATH = [System.Environment]::GetEnvironmentVariable("PATH", "Machine") + ";" + [System.Environment]::GetEnvironmentVariable("PATH", "User")

    if (-not (Get-Command "hf" -ErrorAction SilentlyContinue)) {
        Write-Warn "hf CLI installed but not yet on PATH in this session."
        Write-Warn "Close this terminal, reopen it, then re-run download-models.ps1."
        exit 1
    }

    Write-OK "hf CLI installed and ready."
}

# ------------------------------------------------------------------------------
# POST-DOWNLOAD VALIDATION
# Lightweight presence + non-empty check on ExpectedFiles.
# Deep SHA-256 verification is handled by validate-models.ps1.
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
            Write-Fail "Zero-byte file (incomplete download?): $fileName"
            $allGood = $false
        } else {
            Write-OK "Verified: $fileName  ($(Format-Bytes (Get-Item $fullPath).Length))"
        }
    }
    return $allGood
}

# ------------------------------------------------------------------------------
# STRATEGY A: Direct HTTP - small CDN-backed files (MiniLM vocab, model.onnx)
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
        Write-Info "Size mismatch - re-downloading: $($file.TargetPath)"
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
# STRATEGY B: hf CLI download - large Phi-3 ONNX blobs (Xet-backed)
#
# Flow:
#   1. hf download <repo> --include "<pattern>" --local-dir <tempDir>
#      Downloads files preserving the HF subfolder structure in <tempDir>.
#   2. Copy <tempDir>/<HfSourceSubDir>/* -> <modelsRoot>/<TargetPath>/
#   3. Validate ExpectedFiles.
#   4. Clean up <tempDir>.
# ------------------------------------------------------------------------------

function Invoke-HfDownload($model, [string]$modelsRoot) {
    $targetPath = Join-Path $modelsRoot $model.TargetPath
    $tempDir    = Join-Path $TempRoot  $model.Id

    # Skip if all ExpectedFiles already present (and -Force not set)
    if (-not $Force -and (Test-Path $targetPath)) {
        $allPresent = $true
        foreach ($ef in $model.ExpectedFiles) {
            if (-not (Test-Path (Join-Path $targetPath $ef))) { $allPresent = $false; break }
        }
        if ($allPresent) {
            Write-Skip "Already present: $($model.TargetPath)  (all expected files found)"
            return $true
        }
        Write-Info "Some expected files missing - re-downloading: $($model.TargetPath)"
    }

    Write-Info "Repository:  $($model.HfRepo)"
    Write-Info "HF path:     $($model.HfIncludePattern)"
    Write-Info "Approx size: ~$(Format-Bytes $model.ApproximateSizeBytes)"
    Write-Info "Local path:  $($model.TargetPath)"
    Write-Host ""
    Write-Info "Starting hf download - this will take several minutes for large files..."
    Write-Host ""

    # Prepare directories
    if (Test-Path $tempDir) { Remove-Item $tempDir -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $tempDir    | Out-Null
    New-Item -ItemType Directory -Force -Path $targetPath | Out-Null

    # Run 'hf download'
    # Environment variables transparently honoured:
    #   HF_TOKEN       - set via 'hf login' for gated/private repos
    #   HF_HUB_CACHE   - override the default cache location
    $hfExitCode = 0
    try {
        hf download `
            $model.HfRepo `
            --include $model.HfIncludePattern `
            --local-dir $tempDir

        $hfExitCode = $LASTEXITCODE
    } catch {
        Write-Fail "hf download threw an exception: $_"
        return $false
    }

    if ($hfExitCode -ne 0) {
        Write-Fail "hf download exited with code $hfExitCode"
        Write-Warn "Troubleshooting:"
        Write-Warn "  - Check internet connectivity to huggingface.co"
        Write-Warn "  - Behind a proxy? Set: `$env:HTTPS_PROXY = 'http://proxy:port'"
        Write-Warn "  - For gated models: hf login"
        Write-Warn "  - Partial cache preserved at: $tempDir"
        return $false
    }

    # Copy from HF subfolder into canonical target path
    $sourceSubDir = Join-Path $tempDir $model.HfSourceSubDir

    if (-not (Test-Path $sourceSubDir)) {
        Write-Fail "Expected HF subfolder not found: $($model.HfSourceSubDir)"
        Write-Warn "Actual temp dir contents:"
        Get-ChildItem $tempDir -Recurse | ForEach-Object {
            Write-Host "    $($_.FullName.Replace($tempDir, ''))" -ForegroundColor DarkGray
        }
        Write-Warn "Verify the path still exists at:"
        Write-Warn "  https://huggingface.co/$($model.HfRepo)/tree/main"
        return $false
    }

    Write-Host ""
    Write-Info "Copying model files to target..."

    $sourceFiles = Get-ChildItem $sourceSubDir -Recurse -File
    foreach ($f in $sourceFiles) {
        $rel  = $f.FullName.Substring($sourceSubDir.Length).TrimStart('\', '/')
        $dest = Join-Path $targetPath $rel
        New-Item -ItemType Directory -Force -Path (Split-Path $dest -Parent) | Out-Null
        Copy-Item -Path $f.FullName -Destination $dest -Force
        Write-OK "$rel  ($(Format-Bytes $f.Length))"
    }

    # Validate expected files
    Write-Host ""
    Write-Info "Validating expected files..."
    $valid = Test-ExpectedFiles -targetPath $targetPath -expectedFiles $model.ExpectedFiles

    # Clean up temp
    Remove-Item $tempDir -Recurse -Force -ErrorAction SilentlyContinue

    if ($sourceFiles.Count -eq 0) {
        Write-Fail "No files were copied. The HfIncludePattern matched nothing."
        return $false
    }

    return $valid
}

# ==============================================================================
# ENTRY POINT
# ==============================================================================

Write-Banner "DEVPILOT LOCAL AI MODEL DOWNLOAD SYSTEM"

# Validate manifest
if (-not (Test-Path $ManifestPath)) {
    Write-Fail "model-manifest.json not found at: $ManifestPath"
    Write-Fail "Run bootstrap.ps1 first or verify DevPilot/models/ exists."
    exit 1
}

$manifest = Get-Content $ManifestPath -Raw | ConvertFrom-Json

if ($manifest.SchemaVersion -lt 2) {
    Write-Fail "model-manifest.json is SchemaVersion $($manifest.SchemaVersion). This script requires v2+."
    exit 1
}

# Only check hf CLI if we have entries that require it
if ($manifest.Models | Where-Object { $_.DownloadMethod -eq "huggingface-cli" }) {
    Assert-HfCLI
}

New-Item -ItemType Directory -Force -Path $ModelsRoot | Out-Null
New-Item -ItemType Directory -Force -Path $TempRoot   | Out-Null

$successCount = 0
$failCount    = 0
$skippedCount = 0

foreach ($model in $manifest.Models) {

    # Filter LLM variants
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
    Write-Host "  Category : $($model.Category)"        -ForegroundColor DarkGray
    Write-Host "  Provider : $($model.Provider)"        -ForegroundColor DarkGray
    Write-Host "  Method   : $($model.DownloadMethod)"  -ForegroundColor DarkGray

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
            $ok = Invoke-HfDownload -model $model -modelsRoot $ModelsRoot
        }
        default {
            Write-Fail "Unknown DownloadMethod '$($model.DownloadMethod)' on '$($model.Id)'"
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
    Write-Host "  Results : $successCount downloaded, $skippedCount skipped, 0 failed" -ForegroundColor Gray
    Write-Host ""
    Write-Host "  Next step:" -ForegroundColor Cyan
    Write-Host "    .\scripts\validate-models.ps1" -ForegroundColor Yellow
} else {
    Write-Host "  PARTIAL : $successCount succeeded, $failCount failed, $skippedCount skipped." -ForegroundColor Yellow
    Write-Host ""
    Write-Host "  Troubleshooting tips:" -ForegroundColor Yellow
    Write-Host "    1. Verify internet access to huggingface.co" -ForegroundColor DarkGray
    Write-Host "    2. Proxy: set `$env:HTTPS_PROXY = 'http://your-proxy:port'" -ForegroundColor DarkGray
    Write-Host "    3. Gated models: run  hf login" -ForegroundColor DarkGray
    Write-Host "    4. Re-run a specific variant with -Force:" -ForegroundColor DarkGray
    Write-Host "         .\scripts\download-models.ps1 -Variant cpu -Force" -ForegroundColor Yellow
}

Write-Host ""
Write-Host ("=" * 70) -ForegroundColor Cyan
Write-Host ""

if ($failCount -gt 0) { exit 1 }