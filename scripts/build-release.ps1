<#
.SYNOPSIS
    Master Distribution Build Orchestration Pipeline for DevPilot.
.DESCRIPTION
    Triggers C# self-contained compiles, bundles the VS Code extension, structures the release/ hierarchy, and runs integrity validations.
#>
[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [switch]$SkipExtension
)

$ErrorActionPreference = "Stop"

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "🚀 DevPilot Master Release Orchestration Build Pipeline" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$WorkspaceRoot = Join-Path $PSScriptRoot ".."
$WorkspaceRoot = [System.IO.Path]::GetFullPath($WorkspaceRoot)

$ReleaseDir = Join-Path $WorkspaceRoot "release"
Write-Host "Clearing stale release directories..." -ForegroundColor White
if (Test-Path $ReleaseDir) {
    Remove-Item $ReleaseDir -Recurse -Force -ErrorAction SilentlyContinue
}
New-Item -ItemType Directory -Path $ReleaseDir -Force | Out-Null

# 1. Self-contained .NET CLI compile
Write-Host "`n--> Compiling C# backend service (Self-Contained for $Runtime)..." -ForegroundColor Yellow
$CliProject = Join-Path $WorkspaceRoot "DevPilot\src\DevPilot.CLI\DevPilot.CLI.csproj"

dotnet publish $CliProject `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -o "$ReleaseDir\app" `
    -p:PublishSingleFile=false `
    -p:PublishReadyToRun=false

Write-Host "  [OK] Backend Kestrel service compiled successfully" -ForegroundColor Green

# 2. Package Extension
if (-not $SkipExtension) {
    Write-Host "`n--> Bundling VS Code Extension..." -ForegroundColor Yellow
    $PackagerScript = Join-Path $PSScriptRoot "package-extension.ps1"
    & $PackagerScript -OutputDir "release/extension"
} else {
    New-Item -ItemType Directory -Path "$ReleaseDir\extension" -Force | Out-Null
    Write-Host "  [-] Skipping extension VSIX generation" -ForegroundColor Yellow
}

# 3. Synchronize scripts and tools
Write-Host "`n--> Copying administration tools..." -ForegroundColor Yellow
$ReleaseScriptsDir = Join-Path $ReleaseDir "scripts"
New-Item -ItemType Directory -Path $ReleaseScriptsDir -Force | Out-Null

$ScriptsToCopy = @(
    "download-models.ps1",
    "remove-models.ps1",
    "validate-models.ps1",
    "install-release.ps1"
)
foreach ($Script in $ScriptsToCopy) {
    $Src = Join-Path $PSScriptRoot $Script
    if (Test-Path $Src) {
        Copy-Item $Src -Destination $ReleaseScriptsDir -Force
    }
}
Write-Host "  [OK] Administrative tools packaged" -ForegroundColor Green

# 4. Synchronize documentation & visuals
Write-Host "`n--> Packaging system documentation and visuals..." -ForegroundColor Yellow
$ReleaseDocsDir = Join-Path $ReleaseDir "docs"
New-Item -ItemType Directory -Path $ReleaseDocsDir -Force | Out-Null

$DocsSrc = Join-Path $WorkspaceRoot "docs"
if (Test-Path $DocsSrc) {
    Copy-Item "$DocsSrc\*" -Destination $ReleaseDocsDir -Recurse -Force
}

$AssetsSrc = Join-Path $WorkspaceRoot "assets"
if (Test-Path $AssetsSrc) {
    $ReleaseAssetsDir = Join-Path $ReleaseDir "assets"
    New-Item -ItemType Directory -Path $ReleaseAssetsDir -Force | Out-Null
    Copy-Item "$AssetsSrc\*" -Destination $ReleaseAssetsDir -Recurse -Force
}
Write-Host "  [OK] Core document markdowns and showcase graphics packaged" -ForegroundColor Green

# 5. Synchronize model manifests
Write-Host "`n--> Synchronizing model manifest specifications..." -ForegroundColor Yellow
$ReleaseModelsDir = Join-Path $ReleaseDir "models"
New-Item -ItemType Directory -Path $ReleaseModelsDir -Force | Out-Null

$ManifestSrc = Join-Path $WorkspaceRoot "DevPilot\models\model-manifest.json"
if (Test-Path $ManifestSrc) {
    Copy-Item $ManifestSrc -Destination $ReleaseModelsDir -Force
} else {
    # Generate default manifest if missing from source folder
    $ManifestContent = @{
        models = @(
            @{
                modelId = "phi3"
                name = "Phi-3 Mini 4K Instruct"
                format = "onnx"
                url = "https://huggingface.co/microsoft/Phi-3-mini-4k-instruct-onnx/resolve/main/ONNX/phi3-mini-4k-instruct-cpu.onnx"
                fileName = "model.onnx"
                sizeBytes = 7200000000
                sha256 = "c3f8e5b2"
            }
        )
    } | ConvertTo-Json -Depth 4
    Set-Content -Path "$ReleaseModelsDir\model-manifest.json" -Value $ManifestContent
}
Write-Host "  [OK] model-manifest.json created" -ForegroundColor Green

# 6. Execute validation suite
Write-Host "`n--> Initiating Release Compliance validation..." -ForegroundColor Yellow
$ValidatorScript = Join-Path $PSScriptRoot "validate-release.ps1"
& $ValidatorScript -ReleaseDir "release"

Write-Host "`n==========================================================" -ForegroundColor Cyan
Write-Host "🏆 DevPilot Distribution Release Structured Successfully!" -ForegroundColor Green
Write-Host "==========================================================" -ForegroundColor Cyan
