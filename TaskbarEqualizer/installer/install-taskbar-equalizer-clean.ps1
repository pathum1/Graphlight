# TaskbarEqualizer Simple Installer
# Professional installation script for TaskbarEqualizer

param(
    [string]$InstallPath = "$env:PROGRAMFILES\TaskbarEqualizer",
    [switch]$CreateDesktopShortcut = $true,
    [switch]$AddToStartMenu = $true,
    [switch]$EnableAutoStart = $false,
    [switch]$Silent = $false
)

# Require administrator privileges
if (-NOT ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole] "Administrator")) {
    Write-Host "[ERROR] This installer requires administrator privileges." -ForegroundColor Red
    Write-Host "        Please run PowerShell as Administrator and try again." -ForegroundColor Yellow
    exit 1
}

if (!$Silent) {
    Write-Host "TaskbarEqualizer Installer" -ForegroundColor Cyan
    Write-Host "=========================" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Professional Audio Visualizer for Windows Taskbar" -ForegroundColor White
    Write-Host ""
}

# Configuration
$AppName = "TaskbarEqualizer"
$CompanyName = "Graphlight"
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
    Write-Host "Installation Configuration:" -ForegroundColor Yellow
    Write-Host "  Install Path: $InstallPath" -ForegroundColor White
    Write-Host "  Desktop Shortcut: $CreateDesktopShortcut" -ForegroundColor White
    Write-Host "  Start Menu: $AddToStartMenu" -ForegroundColor White
    Write-Host "  Auto Start: $EnableAutoStart" -ForegroundColor White
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
    Write-Host "[1/8] Creating installation directory..." -ForegroundColor Yellow
    if (!(Test-Path $InstallPath)) {
        New-Item -ItemType Directory -Path $InstallPath -Force | Out-Null
    }
    Write-Host "[OK] Directory created: $InstallPath" -ForegroundColor Green

    # Step 2: Copy application files
    Write-Host "[2/8] Copying application files..." -ForegroundColor Yellow
    Copy-Item "$PublishPath\*" -Destination $InstallPath -Recurse -Force
    Write-Host "[OK] Application files copied" -ForegroundColor Green

    # Step 3: Create desktop shortcut
    if ($CreateDesktopShortcut) {
        Write-Host "[3/8] Creating desktop shortcut..." -ForegroundColor Yellow
        try {
            $WshShell = New-Object -comObject WScript.Shell
            $DesktopPath = [Environment]::GetFolderPath("Desktop")
            $ShortcutPath = Join-Path $DesktopPath "$AppName.lnk"
            $DesktopShortcut = $WshShell.CreateShortcut($ShortcutPath)
            $DesktopShortcut.TargetPath = "$InstallPath\$ExeName"
            $DesktopShortcut.WorkingDirectory = $InstallPath
            $DesktopShortcut.Description = "Professional Audio Visualizer for Windows Taskbar"
            $DesktopShortcut.Save()
            [System.Runtime.Interopservices.Marshal]::ReleaseComObject($WshShell) | Out-Null
            Write-Host "[OK] Desktop shortcut created" -ForegroundColor Green
        } catch {
            Write-Host "[WARNING] Could not create desktop shortcut: $($_.Exception.Message)" -ForegroundColor Yellow
            Write-Host "[INFO] You can manually create a shortcut to: $InstallPath\$ExeName" -ForegroundColor Cyan
        }
    } else {
        Write-Host "[3/8] Skipping desktop shortcut..." -ForegroundColor Gray
    }

    # Step 4: Create Start Menu shortcuts
    if ($AddToStartMenu) {
        Write-Host "[4/8] Creating Start Menu shortcuts..." -ForegroundColor Yellow
        try {
            $WshShell = New-Object -comObject WScript.Shell
            $StartMenuPath = "$env:ALLUSERSPROFILE\Microsoft\Windows\Start Menu\Programs\$AppName"
            if (!(Test-Path $StartMenuPath)) {
                New-Item -ItemType Directory -Path $StartMenuPath -Force | Out-Null
            }
            
            # Main shortcut
            $MainShortcutPath = Join-Path $StartMenuPath "$AppName.lnk"
            $StartMenuShortcut = $WshShell.CreateShortcut($MainShortcutPath)
            $StartMenuShortcut.TargetPath = "$InstallPath\$ExeName"
            $StartMenuShortcut.WorkingDirectory = $InstallPath
            $StartMenuShortcut.Description = "Professional Audio Visualizer for Windows Taskbar"
            $StartMenuShortcut.Save()
            
            # Uninstall shortcut
            $UninstallShortcutPath = Join-Path $StartMenuPath "Uninstall $AppName.lnk"
            $UninstallShortcut = $WshShell.CreateShortcut($UninstallShortcutPath)
            $UninstallShortcut.TargetPath = "powershell.exe"
            $UninstallShortcut.Arguments = "-ExecutionPolicy Bypass -File `"$InstallPath\uninstall.ps1`""
            $UninstallShortcut.WorkingDirectory = $InstallPath
            $UninstallShortcut.Description = "Uninstall TaskbarEqualizer"
            $UninstallShortcut.Save()
            
            [System.Runtime.Interopservices.Marshal]::ReleaseComObject($WshShell) | Out-Null
            Write-Host "[OK] Start Menu shortcuts created" -ForegroundColor Green
        } catch {
            Write-Host "[WARNING] Could not create Start Menu shortcuts: $($_.Exception.Message)" -ForegroundColor Yellow
            Write-Host "[INFO] Application installed successfully without shortcuts" -ForegroundColor Cyan
        }
    } else {
        Write-Host "[4/8] Skipping Start Menu shortcuts..." -ForegroundColor Gray
    }

    # Step 5: Registry entries
    Write-Host "[5/8] Creating registry entries..." -ForegroundColor Yellow
    $RegPath = "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\$AppName"
    New-Item -Path $RegPath -Force | Out-Null
    Set-ItemProperty -Path $RegPath -Name "DisplayName" -Value $AppName
    Set-ItemProperty -Path $RegPath -Name "DisplayVersion" -Value "1.0.0"
    Set-ItemProperty -Path $RegPath -Name "Publisher" -Value $CompanyName
    Set-ItemProperty -Path $RegPath -Name "InstallLocation" -Value $InstallPath
    Set-ItemProperty -Path $RegPath -Name "UninstallString" -Value "`"$InstallPath\uninstall.ps1`""
    Set-ItemProperty -Path $RegPath -Name "DisplayIcon" -Value "$InstallPath\$ExeName"
    Set-ItemProperty -Path $RegPath -Name "NoModify" -Value 1 -Type DWord
    Set-ItemProperty -Path $RegPath -Name "NoRepair" -Value 1 -Type DWord
    
    # Calculate installed size
    $Size = (Get-ChildItem $InstallPath -Recurse | Measure-Object -Property Length -Sum).Sum
    $SizeKB = [math]::Round($Size / 1KB)
    Set-ItemProperty -Path $RegPath -Name "EstimatedSize" -Value $SizeKB -Type DWord
    
    Write-Host "[OK] Registry entries created" -ForegroundColor Green

    # Step 6: Auto-start configuration
    if ($EnableAutoStart) {
        Write-Host "[6/8] Configuring auto-start..." -ForegroundColor Yellow
        $AutoStartPath = "HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run"
        Set-ItemProperty -Path $AutoStartPath -Name $AppName -Value "`"$InstallPath\$ExeName`" --minimized"
        Write-Host "[OK] Auto-start configured" -ForegroundColor Green
    } else {
        Write-Host "[6/8] Skipping auto-start configuration..." -ForegroundColor Gray
    }

    # Step 7: Create uninstaller
    Write-Host "[7/8] Creating uninstaller..." -ForegroundColor Yellow
    $UninstallScript = @"
# TaskbarEqualizer Uninstaller
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

    # Remove auto-start
    Remove-ItemProperty -Path "HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run" -Name "$AppName" -ErrorAction SilentlyContinue

    # Remove shortcuts
    Remove-Item "`$env:USERPROFILE\Desktop\$AppName.lnk" -ErrorAction SilentlyContinue
    Remove-Item "`$env:ALLUSERSPROFILE\Microsoft\Windows\Start Menu\Programs\$AppName" -Recurse -ErrorAction SilentlyContinue

    # Remove registry entries
    Remove-Item "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\$AppName" -Recurse -ErrorAction SilentlyContinue

    # Remove application files
    Set-Location `$env:TEMP
    Remove-Item "$InstallPath" -Recurse -Force -ErrorAction SilentlyContinue

    Write-Host "[OK] TaskbarEqualizer uninstalled successfully" -ForegroundColor Green
} catch {
    Write-Host "[ERROR] Uninstall error: `$(`$_.Exception.Message)" -ForegroundColor Red
}

if (!`$Silent) {
    Read-Host "Press Enter to close"
}
"@
    $UninstallScript | Out-File -FilePath "$InstallPath\uninstall.ps1" -Encoding UTF8
    Write-Host "[OK] Uninstaller created" -ForegroundColor Green

    # Step 8: Create settings directory
    Write-Host "[8/8] Creating settings directory..." -ForegroundColor Yellow
    $SettingsPath = "$env:APPDATA\TaskbarEqualizer"
    if (!(Test-Path $SettingsPath)) {
        New-Item -ItemType Directory -Path $SettingsPath -Force | Out-Null
    }
    Write-Host "[OK] Settings directory created: $SettingsPath" -ForegroundColor Green

    # Installation complete
    Write-Host ""
    Write-Host "Installation completed successfully!" -ForegroundColor Green
    Write-Host ""
    Write-Host "Installation Summary:" -ForegroundColor Cyan
    Write-Host "  Installed to: $InstallPath" -ForegroundColor White
    Write-Host "  Size: $([math]::Round($Size / 1MB, 1)) MB" -ForegroundColor White
    Write-Host "  Settings: $SettingsPath" -ForegroundColor White
    Write-Host ""
    Write-Host "Launch TaskbarEqualizer:" -ForegroundColor Yellow
    Write-Host "  - Double-click desktop shortcut" -ForegroundColor White
    Write-Host "  - Start Menu -> $AppName" -ForegroundColor White
    Write-Host "  - Run: `"$InstallPath\$ExeName`"" -ForegroundColor White
    Write-Host ""
    Write-Host "To uninstall:" -ForegroundColor Yellow
    Write-Host "  - Control Panel -> Programs -> $AppName" -ForegroundColor White
    Write-Host "  - Or run: `"$InstallPath\uninstall.ps1`"" -ForegroundColor White
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
    Write-Host "Stack trace: $($_.Exception.StackTrace)" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "Thank you for using TaskbarEqualizer!" -ForegroundColor Cyan