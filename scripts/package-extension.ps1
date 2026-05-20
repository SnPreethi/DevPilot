# DevPilot Extension Packager

param(
    [string]$OutputDir = 'release/extension',
    [switch]$SkipRestore
)

$ErrorActionPreference = 'Stop'

Write-Host '==========================================================' -ForegroundColor 'Cyan'
Write-Host '📦 DevPilot VS Code Extension Packager' -ForegroundColor 'Cyan'
Write-Host '==========================================================' -ForegroundColor 'Cyan'

$ExtensionDir = Join-Path $PSScriptRoot '..\DevPilot.VSCodeExtension'
$ExtensionDir = [System.IO.Path]::GetFullPath($ExtensionDir)

if (-not (Test-Path $ExtensionDir)) {
    Write-Error 'Extension directory not found'
}

Push-Location $ExtensionDir
try {
    # 1. Restore modules
    if (-not $SkipRestore.IsPresent) {
        Write-Host '--> Restoring extension npm packages...' -ForegroundColor 'Yellow'
        npm ci
    }

    # 2. Bundle Extension JS
    Write-Host '--> Bundling TypeScript source files with esbuild...' -ForegroundColor 'Yellow'
    npm run package

    # 3. Create output directories
    $ReleaseExtensionPath = Join-Path $PSScriptRoot ('..\'+ $OutputDir)
    $ReleaseExtensionPath = [System.IO.Path]::GetFullPath($ReleaseExtensionPath)
    if (-not (Test-Path $ReleaseExtensionPath)) {
        New-Item -ItemType 'Directory' -Path $ReleaseExtensionPath -Force | Out-Null
    }

    # 4. Assemble VSIX Package
    Write-Host '--> Generating VSIX package with vsce...' -ForegroundColor 'Yellow'
    $OutputFile = Join-Path $ReleaseExtensionPath 'devpilot-vscode-0.1.0.vsix'
    
    npx -y @vscode/vsce package --no-dependencies --out $OutputFile

    Write-Host '  [OK] VSIX packaging completed successfully!' -ForegroundColor 'Green'
    Write-Host 'Package Output: ' -NoNewline -ForegroundColor 'Green'
    Write-Host $OutputFile -ForegroundColor 'Green'
}
finally {
    Pop-Location
}
