# validate-prerequisites.ps1
# Validates developer environment dependencies for DevPilot.

$ErrorActionPreference = "Stop"

Write-Host "======================================================================" -ForegroundColor Cyan
Write-Host "            DEVPILOT PREREQUISITE VALIDATION SYSTEM" -ForegroundColor Cyan
Write-Host "======================================================================" -ForegroundColor Cyan

$allPassed = $true

# Helper function for printing results
function Write-ValidationResult {
    param (
        [string]$Name,
        [bool]$Success,
        [string]$Details,
        [string]$Remediation = ""
    )
    if ($Success) {
        Write-Host "[ PASS ] " -NoNewline -ForegroundColor Green
        Write-Host "$Name - $Details"
    } else {
        Write-Host "[ FAIL ] " -NoNewline -ForegroundColor Red
        Write-Host "$Name - $Details" -ForegroundColor Red
        if ($Remediation) {
            Write-Host "         Remediation: $Remediation" -ForegroundColor Yellow
        }
        $script:allPassed = $false
    }
}

# 1. Windows OS check
$isWindows = $IsWindows -or ($env:OS -like "*Windows*")
Write-ValidationResult "Windows OS" $isWindows "Windows 10/11 is required for DirectML execution." "Ensure you are running on a Windows 10/11 environment."

# 2. .NET 8 SDK check
try {
    $dotnetVersion = & dotnet --version 2>$null
    $majorDotnet = [int]($dotnetVersion.Split('.')[0])
    if ($majorDotnet -ge 8) {
        Write-ValidationResult ".NET SDK" $true "Found .NET $dotnetVersion (.NET 8.0+ supported)."
    } else {
        Write-ValidationResult ".NET SDK" $false "Found .NET $dotnetVersion. .NET 8.0 SDK or newer is required." "Download and install .NET 8.0 SDK or newer from https://dotnet.microsoft.com/download/dotnet"
    }
} catch {
    Write-ValidationResult ".NET SDK" $false "Not found in PATH." "Download and install .NET 8.0 SDK from https://dotnet.microsoft.com/download/dotnet/8.0"
}

# 3. Node.js check
try {
    $nodeVersion = & node -v 2>$null
    $cleanNodeVer = $nodeVersion.Trim().TrimStart('v')
    $majorVer = [int]($cleanNodeVer.Split('.')[0])
    if ($majorVer -ge 18) {
        Write-ValidationResult "Node.js" $true "Found Node.js $nodeVersion (v18+ supported)."
    } else {
        Write-ValidationResult "Node.js" $false "Found Node.js $nodeVersion. Node 18.x or 20.x+ is required." "Upgrade your Node.js version from https://nodejs.org"
    }
} catch {
    Write-ValidationResult "Node.js" $false "Not found in PATH." "Download and install Node.js (LTS version) from https://nodejs.org"
}

# 4. Git check
try {
    $gitVer = & git --version 2>$null
    Write-ValidationResult "Git" $true "Found $gitVer."
} catch {
    Write-ValidationResult "Git" $false "Not found in PATH." "Download and install Git for Windows from https://git-scm.com"
}

# 5. VS Code check
try {
    $codeVer = & code --version 2>$null
    $lines = $codeVer -split "`n"
    Write-ValidationResult "VS Code" $true "Found VS Code v$($lines[0])."
} catch {
    Write-ValidationResult "VS Code" $false "Command 'code' not found in PATH." "Ensure VS Code is installed and 'Add to PATH' was checked during setup."
}

Write-Host "----------------------------------------------------------------------" -ForegroundColor Cyan
if ($allPassed) {
    Write-Host "SUCCESS: All developer environment prerequisites met!" -ForegroundColor Green
    exit 0
} else {
    Write-Host "ERROR: Missing or unsupported prerequisites detected. Please resolve above." -ForegroundColor Red
    exit 1
}
