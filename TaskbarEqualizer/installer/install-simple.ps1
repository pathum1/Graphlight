# TaskbarEqualizer Ultra Simple Installer
# Minimal installation script without COM objects

param(
    [string]$InstallPath = "$env:PROGRAMFILES\TaskbarEqualizer",
    [switch]$Silent = $false
)

# Require administrator privileges
if (-NOT ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole] "Administrator")) {
    Write-Host "[ERROR] This installer requires administrator privileges." -ForegroundColor Red
    Write-Host "        Please run PowerShell as Administrator and try again." -ForegroundColor Yellow
    exit 1
}

if (!$Silent) {
    Write-Host "TaskbarEqualizer Simple Installer" -ForegroundColor Cyan
    Write-Host "=================================" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Professional Audio Visualizer for Windows Taskbar" -ForegroundColor White
    Write-Host ""
}

# Configuration
$AppName = "TaskbarEqualizer"
$ExeName = "TaskbarEqualizer.exe"
$SourcePath = Split-Path -Parent $MyInvocation.MyCommand.Path
$PublishPath = Join-Path (Split-Path -Parent $SourcePath) "publish\TaskbarEqualizer"

# Verify source files exist
if (!(Test-Path "$PublishPath\$ExeName")) {
    Write-Host "[ERROR] Source files not found!" -ForegroundColor Red
    Write-Host "        Expected: $PublishPath\$ExeName" -ForegroundColor Yellow
    Write-Host "        Please run: dotnet publish first" -ForegroundColor Yellow
    exit 1
}

if (!$Silent) {
    Write-Host "Install Path: $InstallPath" -ForegroundColor Yellow
    Write-Host ""
    
    $confirm = Read-Host "Proceed with installation? (Y/N)"
    if ($confirm -notmatch "^[Yy]") {
        Write-Host "Installation cancelled." -ForegroundColor Yellow
        exit 0
    }
    Write-Host ""
}

try {
    # Step 1: Create installation directory
    Write-Host "[1/4] Creating installation directory..." -ForegroundColor Yellow
    if (!(Test-Path $InstallPath)) {
        New-Item -ItemType Directory -Path $InstallPath -Force | Out-Null
    }
    Write-Host "[OK] Directory created: $InstallPath" -ForegroundColor Green

    # Step 2: Copy application files
    Write-Host "[2/4] Copying application files..." -ForegroundColor Yellow
    Copy-Item "$PublishPath\*" -Destination $InstallPath -Recurse -Force
    Write-Host "[OK] Application files copied" -ForegroundColor Green

    # Step 3: Registry entries for uninstall
    Write-Host "[3/4] Creating registry entries..." -ForegroundColor Yellow
    $RegPath = "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\$AppName"
    New-Item -Path $RegPath -Force | Out-Null
    Set-ItemProperty -Path $RegPath -Name "DisplayName" -Value $AppName
    Set-ItemProperty -Path $RegPath -Name "DisplayVersion" -Value "1.0.0"
    Set-ItemProperty -Path $RegPath -Name "Publisher" -Value "Graphlight"
    Set-ItemProperty -Path $RegPath -Name "InstallLocation" -Value $InstallPath
    Set-ItemProperty -Path $RegPath -Name "UninstallString" -Value "powershell.exe -ExecutionPolicy Bypass -File `"$InstallPath\uninstall.ps1`""
    Set-ItemProperty -Path $RegPath -Name "DisplayIcon" -Value "$InstallPath\$ExeName"
    Set-ItemProperty -Path $RegPath -Name "NoModify" -Value 1 -Type DWord
    Set-ItemProperty -Path $RegPath -Name "NoRepair" -Value 1 -Type DWord
    
    # Calculate installed size
    $Size = (Get-ChildItem $InstallPath -Recurse | Measure-Object -Property Length -Sum).Sum
    $SizeKB = [math]::Round($Size / 1KB)
    Set-ItemProperty -Path $RegPath -Name "EstimatedSize" -Value $SizeKB -Type DWord
    
    Write-Host "[OK] Registry entries created" -ForegroundColor Green

    # Step 4: Create uninstaller
    Write-Host "[4/4] Creating uninstaller..." -ForegroundColor Yellow
    $UninstallScript = @"
# TaskbarEqualizer Simple Uninstaller
param([switch]`$Silent = `$false)

if (-NOT ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole] "Administrator")) {
    Write-Host "[ERROR] Uninstaller requires administrator privileges." -ForegroundColor Red
    exit 1
}

if (!`$Silent) {
    Write-Host "TaskbarEqualizer Uninstaller" -ForegroundColor Red
    `$confirm = Read-Host "Are you sure you want to uninstall TaskbarEqualizer? (Y/N)"
    if (`$confirm -notmatch "^[Yy]") {
        Write-Host "Uninstall cancelled." -ForegroundColor Yellow
        exit 0
    }
}

try {
    Write-Host "Uninstalling TaskbarEqualizer..." -ForegroundColor Yellow
    
    # Stop application if running
    Get-Process -Name "TaskbarEqualizer" -ErrorAction SilentlyContinue | Stop-Process -Force

    # Remove auto-start (if exists)
    Remove-ItemProperty -Path "HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run" -Name "$AppName" -ErrorAction SilentlyContinue

    # Remove registry entries
    Remove-Item "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\$AppName" -Recurse -ErrorAction SilentlyContinue

    # Remove application files
    Set-Location `$env:TEMP
    Remove-Item "$InstallPath" -Recurse -Force -ErrorAction SilentlyContinue

    Write-Host "[OK] TaskbarEqualizer uninstalled successfully" -ForegroundColor Green
    Write-Host ""
    Write-Host "Note: You may need to manually remove any desktop or Start Menu shortcuts" -ForegroundColor Yellow
} catch {
    Write-Host "[ERROR] Uninstall error: `$(`$_.Exception.Message)" -ForegroundColor Red
}

if (!`$Silent) {
    Read-Host "Press Enter to close"
}
"@
    $UninstallScript | Out-File -FilePath "$InstallPath\uninstall.ps1" -Encoding UTF8
    Write-Host "[OK] Uninstaller created" -ForegroundColor Green

    # Create settings directory
    $SettingsPath = "$env:APPDATA\TaskbarEqualizer"
    if (!(Test-Path $SettingsPath)) {
        New-Item -ItemType Directory -Path $SettingsPath -Force | Out-Null
    }

    # Installation complete
    Write-Host ""
    Write-Host "Installation completed successfully!" -ForegroundColor Green
    Write-Host ""
    Write-Host "Installation Summary:" -ForegroundColor Cyan
    Write-Host "  Installed to: $InstallPath" -ForegroundColor White
    Write-Host "  Size: $([math]::Round($Size / 1MB, 1)) MB" -ForegroundColor White
    Write-Host "  Settings: $SettingsPath" -ForegroundColor White
    Write-Host ""
    Write-Host "To run TaskbarEqualizer:" -ForegroundColor Yellow
    Write-Host "`"$InstallPath\$ExeName`"" -ForegroundColor White
    Write-Host ""
    Write-Host "To uninstall:" -ForegroundColor Yellow
    Write-Host "  - Control Panel -> Programs -> $AppName" -ForegroundColor White
    Write-Host "  - Or run: `"$InstallPath\uninstall.ps1`"" -ForegroundColor White
    Write-Host ""
    Write-Host "Create shortcuts manually:" -ForegroundColor Yellow
    Write-Host "  - Right-click TaskbarEqualizer.exe -> Send to -> Desktop" -ForegroundColor White
    Write-Host ""
    
    if (!$Silent) {
        $launch = Read-Host "Launch TaskbarEqualizer now? (Y/N)"
        if ($launch -match "^[Yy]") {
            Write-Host "Launching TaskbarEqualizer..." -ForegroundColor Green
            Start-Process "$InstallPath\$ExeName"
        }
    }

} catch {
    Write-Host "[ERROR] Installation failed: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "Thank you for using TaskbarEqualizer!" -ForegroundColor Cyan