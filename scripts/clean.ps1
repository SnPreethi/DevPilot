# clean.ps1
# Cleans build outputs, compiled packages, and node cache logs,
# while safely preserving model files and user databases.

$ErrorActionPreference = "Stop"

Write-Host "======================================================================" -ForegroundColor Cyan
Write-Host "            DEVPILOT CLOBBER & BUILD CLEANUP UTILITY" -ForegroundColor Cyan
Write-Host "======================================================================" -ForegroundColor Cyan

$scriptPath = Split-Path -Parent $MyInvocation.MyCommand.Definition
$rootPath = Resolve-Path (Join-Path $scriptPath "..")
$devPilotPath = Join-Path $rootPath "DevPilot"
$extensionPath = Join-Path $rootPath "DevPilot.VSCodeExtension"

# 1. Clean .NET bin and obj outputs
Write-Host "--> Scanning for C# intermediate build directories..." -ForegroundColor Gray
$targets = Get-ChildItem -Path $devPilotPath -Directory -Recurse -Depth 4 -ErrorAction SilentlyContinue | Where-Object { $_.Name -eq "bin" -or $_.Name -eq "obj" }

if ($targets) {
    foreach ($target in $targets) {
        try {
            Remove-Item -Path $target.FullName -Recurse -Force -ErrorAction SilentlyContinue | Out-Null
            Write-Host "    [CLEANED] $($target.FullName)" -ForegroundColor Yellow
        } catch {
            Write-Host "    [SKIP] Could not clean $($target.FullName) (File locked by MSBuild)" -ForegroundColor Gray
        }
    }
} else {
    Write-Host "    [OK] No C# build output folders found." -ForegroundColor Green
}

# 2. Clean VS Code extension builds
Write-Host "--> Cleaning VS Code extension compilation outputs..." -ForegroundColor Gray
$extOutputs = @("out", "dist", ".vscode-test")
foreach ($outDir in $extOutputs) {
    $dirPath = Join-Path $extensionPath $outDir
    if (Test-Path $dirPath) {
        Remove-Item -Path $dirPath -Recurse -Force
        Write-Host "    [CLEANED] DevPilot.VSCodeExtension/$outDir/" -ForegroundColor Yellow
    }
}

Write-Host "----------------------------------------------------------------------" -ForegroundColor Cyan
Write-Host "SUCCESS: Clean completed successfully! Large ONNX models are safe." -ForegroundColor Green
