# TaskbarEqualizer Installer Build Script
# Builds professional MSI installer using WiX Toolset

param(
    [string]$Configuration = "Release",
    [string]$Platform = "x64",
    [string]$OutputPath = ".\output",
    [switch]$Clean = $false,
    [switch]$Verbose = $false
)

# Script configuration
$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$SolutionDir = Split-Path -Parent $ScriptDir
$InstallerProject = "$ScriptDir\TaskbarEqualizer.Installer\TaskbarEqualizer.Installer.wixproj"

Write-Host "🏗️ TaskbarEqualizer Installer Build Script" -ForegroundColor Cyan
Write-Host "=============================================" -ForegroundColor Cyan
Write-Host ""

# Validate prerequisites
Write-Host "🔍 Checking prerequisites..." -ForegroundColor Yellow

# Check if WiX Toolset is installed
try {
    $wixPath = Get-Command "candle.exe" -ErrorAction Stop
    Write-Host "✅ WiX Toolset found: $($wixPath.Source)" -ForegroundColor Green
} catch {
    Write-Host "❌ WiX Toolset not found. Please install WiX Toolset v3.11 or later." -ForegroundColor Red
    Write-Host "   Download from: https://github.com/wixtoolset/wix3/releases" -ForegroundColor Yellow
    exit 1
}

# Check if .NET SDK is available
try {
    $dotnetVersion = dotnet --version
    Write-Host "✅ .NET SDK found: $dotnetVersion" -ForegroundColor Green
} catch {
    Write-Host "❌ .NET SDK not found. Please install .NET 8.0 SDK or later." -ForegroundColor Red
    exit 1
}

# Clean previous builds if requested
if ($Clean) {
    Write-Host "🧹 Cleaning previous builds..." -ForegroundColor Yellow
    
    if (Test-Path $OutputPath) {
        Remove-Item $OutputPath -Recurse -Force
        Write-Host "✅ Cleaned output directory" -ForegroundColor Green
    }
    
    # Clean solution
    Push-Location $SolutionDir
    try {
        dotnet clean --configuration $Configuration --verbosity minimal
        Write-Host "✅ Cleaned solution" -ForegroundColor Green
    } finally {
        Pop-Location
    }
}

# Create output directory
if (!(Test-Path $OutputPath)) {
    New-Item -ItemType Directory -Path $OutputPath -Force | Out-Null
    Write-Host "📁 Created output directory: $OutputPath" -ForegroundColor Green
}

# Step 1: Build the main application
Write-Host "🔨 Building TaskbarEqualizer application..." -ForegroundColor Yellow

Push-Location $SolutionDir
try {
    $buildArgs = @(
        "build"
        "--configuration", $Configuration
        "--runtime", "win-x64"
        "--self-contained", "false"
        "--verbosity", ($Verbose ? "normal" : "minimal")
    )
    
    & dotnet @buildArgs
    
    if ($LASTEXITCODE -ne 0) {
        throw "Application build failed with exit code $LASTEXITCODE"
    }
    
    Write-Host "✅ Application build completed successfully" -ForegroundColor Green
} finally {
    Pop-Location
}

# Step 2: Prepare installer files
Write-Host "📦 Preparing installer files..." -ForegroundColor Yellow

$AppOutputDir = "$SolutionDir\src\TaskbarEqualizer\bin\$Configuration\net8.0-windows"
$InstallerResourcesDir = "$ScriptDir\TaskbarEqualizer.Installer\Resources"

# Verify application files exist
$RequiredFiles = @(
    "TaskbarEqualizer.exe",
    "TaskbarEqualizer.Core.dll",
    "TaskbarEqualizer.SystemTray.dll",
    "TaskbarEqualizer.Configuration.dll"
)

foreach ($file in $RequiredFiles) {
    $filePath = Join-Path $AppOutputDir $file
    if (!(Test-Path $filePath)) {
        Write-Host "❌ Required file not found: $file" -ForegroundColor Red
        Write-Host "   Expected path: $filePath" -ForegroundColor Yellow
        exit 1
    }
}

Write-Host "✅ All required application files found" -ForegroundColor Green

# Create installer resources directory if it doesn't exist
if (!(Test-Path $InstallerResourcesDir)) {
    New-Item -ItemType Directory -Path $InstallerResourcesDir -Force | Out-Null
}

# Step 3: Build the installer
Write-Host "🏗️ Building MSI installer..." -ForegroundColor Yellow

Push-Location $ScriptDir
try {
    # Set environment variables for WiX
    $env:SolutionDir = $SolutionDir
    $env:Configuration = $Configuration
    $env:Platform = $Platform
    
    # Build using MSBuild (WiX integrates with MSBuild)
    $msbuildArgs = @(
        $InstallerProject
        "/p:Configuration=$Configuration"
        "/p:Platform=$Platform"
        "/p:OutputPath=$OutputPath"
        "/p:SolutionDir=$SolutionDir"
        "/verbosity:$($Verbose ? 'normal' : 'minimal')"
    )
    
    & dotnet build @msbuildArgs
    
    if ($LASTEXITCODE -ne 0) {
        throw "Installer build failed with exit code $LASTEXITCODE"
    }
    
    Write-Host "✅ Installer build completed successfully" -ForegroundColor Green
} finally {
    Pop-Location
}

# Step 4: Verify installer output
Write-Host "🔍 Verifying installer output..." -ForegroundColor Yellow

$InstallerFile = Get-ChildItem -Path $OutputPath -Filter "*.msi" | Select-Object -First 1

if ($InstallerFile) {
    $InstallerPath = $InstallerFile.FullName
    $InstallerSize = [math]::Round($InstallerFile.Length / 1MB, 2)
    
    Write-Host "✅ Installer created successfully:" -ForegroundColor Green
    Write-Host "   📄 File: $($InstallerFile.Name)" -ForegroundColor Cyan
    Write-Host "   📁 Path: $InstallerPath" -ForegroundColor Cyan
    Write-Host "   📏 Size: $InstallerSize MB" -ForegroundColor Cyan
    
    # Optional: Generate checksums
    $MD5Hash = (Get-FileHash -Path $InstallerPath -Algorithm MD5).Hash
    $SHA256Hash = (Get-FileHash -Path $InstallerPath -Algorithm SHA256).Hash
    
    Write-Host "   🔐 MD5: $MD5Hash" -ForegroundColor Cyan
    Write-Host "   🔐 SHA256: $SHA256Hash" -ForegroundColor Cyan
    
    # Save checksums to file
    $ChecksumFile = Join-Path $OutputPath "checksums.txt"
    @"
TaskbarEqualizer Installer Checksums
Generated: $(Get-Date -Format "yyyy-MM-dd HH:mm:ss")

File: $($InstallerFile.Name)
Size: $InstallerSize MB
MD5: $MD5Hash
SHA256: $SHA256Hash
"@ | Out-File -FilePath $ChecksumFile -Encoding UTF8
    
    Write-Host "   📋 Checksums saved to: checksums.txt" -ForegroundColor Cyan
} else {
    Write-Host "❌ Installer file not found in output directory" -ForegroundColor Red
    exit 1
}

# Step 5: Optional - Test installer
Write-Host ""
Write-Host "🎉 Build completed successfully!" -ForegroundColor Green
Write-Host ""
Write-Host "📋 Next steps:" -ForegroundColor Yellow
Write-Host "   1. Test the installer: msiexec /i `"$InstallerPath`" /l*v install.log" -ForegroundColor White
Write-Host "   2. Silent install: msiexec /i `"$InstallerPath`" /quiet" -ForegroundColor White
Write-Host "   3. Uninstall: msiexec /x `"$InstallerPath`" /quiet" -ForegroundColor White
Write-Host ""
Write-Host "📁 Output location: $OutputPath" -ForegroundColor Cyan
Write-Host "✨ Happy installing! 🚀" -ForegroundColor Green