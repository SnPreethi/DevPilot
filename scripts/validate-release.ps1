# DevPilot Package Validator

param(
    [string]$ReleaseDir = 'release'
)

$ErrorActionPreference = 'Stop'

Write-Host '==========================================================' -ForegroundColor 'Cyan'
Write-Host '🔍 DevPilot Distribution Package Integrity Validator' -ForegroundColor 'Cyan'
Write-Host '==========================================================' -ForegroundColor 'Cyan'

$ReleasePath = Join-Path $PSScriptRoot ('..\'+ $ReleaseDir)
$ReleasePath = [System.IO.Path]::GetFullPath($ReleasePath)

if (-not (Test-Path $ReleasePath)) {
    Write-Error 'Release folder not found'
}

$global:ErrorsCount = 0

function Verify-Path {
    param([string]$Path, [string]$Description)
    if (Test-Path $Path) {
        Write-Host '  [✓] ' -NoNewline -ForegroundColor 'Green'
        Write-Host $Description -NoNewline -ForegroundColor 'Green'
        Write-Host ' exists' -ForegroundColor 'Green'
    } else {
        Write-Host '  [✗] ' -NoNewline -ForegroundColor 'Red'
        Write-Host $Description -NoNewline -ForegroundColor 'Red'
        Write-Host ' IS MISSING: ' -NoNewline -ForegroundColor 'Red'
        Write-Host $Path -ForegroundColor 'Red'
        $global:ErrorsCount = $global:ErrorsCount + 1
    }
}

# 1. Validate Core Folders
Write-Host ''
Write-Host '--> Verifying directory structure...' -ForegroundColor 'Yellow'
$Folders = @('app', 'extension', 'scripts', 'docs', 'models')
foreach ($Folder in $Folders) {
    $Sub = Join-Path $ReleasePath $Folder
    Verify-Path -Path $Sub -Description $Folder
}

# 2. Validate App Assemblies
Write-Host ''
Write-Host '--> Verifying compiled backend assemblies...' -ForegroundColor 'Yellow'
$BackendExecutable = Join-Path $ReleasePath 'app\DevPilot.CLI.exe'
Verify-Path -Path $BackendExecutable -Description 'Backend CLI Service executable'

$CoreDll = Join-Path $ReleasePath 'app\DevPilot.Core.dll'
Verify-Path -Path $CoreDll -Description 'DevPilot.Core runtime assembly'

$SqliteDll = Join-Path $ReleasePath 'app\Microsoft.Data.Sqlite.dll'
Verify-Path -Path $SqliteDll -Description 'Microsoft.Data.Sqlite runtime assembly'

# 3. Validate Extension
Write-Host ''
Write-Host '--> Verifying packaged VS Code extension...' -ForegroundColor 'Yellow'
$VsixPackage = Join-Path $ReleasePath 'extension\devpilot-vscode-0.1.0.vsix'
Verify-Path -Path $VsixPackage -Description 'Packaged VS Code VSIX file'

# 4. Validate Script Toolings
Write-Host ''
Write-Host '--> Verifying administration and setup scripts...' -ForegroundColor 'Yellow'
$DownloadScript = Join-Path $ReleasePath 'scripts\download-models.ps1'
Verify-Path -Path $DownloadScript -Description 'Model provisioning script'

$InstallerScript = Join-Path $ReleasePath 'scripts\install-release.ps1'
Verify-Path -Path $InstallerScript -Description 'Release installation orchestrator'

# 5. Validate Manifest and Settings configs
Write-Host ''
Write-Host '--> Verifying model configurations...' -ForegroundColor 'Yellow'
$Manifest = Join-Path $ReleasePath 'models\model-manifest.json'
Verify-Path -Path $Manifest -Description 'Manifest descriptor model-manifest.json'

if ($global:ErrorsCount -eq 0) {
    Write-Host ''
    Write-Host '🏆 Distribution release is 100% COMPLIANT and validated successfully!' -ForegroundColor 'Green'
    exit 0
} else {
    Write-Host ''
    Write-Host '🚨 Validation failed!' -ForegroundColor 'Red'
    exit 1
}
