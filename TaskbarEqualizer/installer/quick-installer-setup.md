# 🚀 Quick Installer Setup Guide

## **Option 1: Simple EXE Installer (Fastest)**

If you want to get started quickly without WiX complexity, here's a simple approach using built-in Windows tools:

### **1. Create Self-Contained Deployment**
```powershell
cd "J:\Graphlight\TaskbarEqualizer"

# Publish as self-contained executable
dotnet publish src/TaskbarEqualizer `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    --output "publish\TaskbarEqualizer" `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true
```

### **2. Create Simple Installer Script**
```powershell
# Create installer.ps1
@'
param([string]$InstallPath = "$env:PROGRAMFILES\TaskbarEqualizer")

Write-Host "Installing TaskbarEqualizer to: $InstallPath"

# Create directory
New-Item -ItemType Directory -Path $InstallPath -Force

# Copy files
Copy-Item "TaskbarEqualizer.exe" -Destination $InstallPath
Copy-Item "*.dll" -Destination $InstallPath -ErrorAction SilentlyContinue

# Create shortcuts
$WshShell = New-Object -comObject WScript.Shell
$Shortcut = $WshShell.CreateShortcut("$env:USERPROFILE\Desktop\TaskbarEqualizer.lnk")
$Shortcut.TargetPath = "$InstallPath\TaskbarEqualizer.exe"
$Shortcut.Save()

Write-Host "Installation complete!"
'@ | Out-File -FilePath "installer.ps1"
```

## **Option 2: NSIS Installer (Lightweight)**

### **1. Install NSIS**
- Download from: https://nsis.sourceforge.io/Download
- Install NSIS with default options

### **2. Create NSIS Script**
```nsis
; TaskbarEqualizer.nsi
!define APPNAME "TaskbarEqualizer"
!define COMPANYNAME "Graphlight"
!define DESCRIPTION "Professional Audio Visualizer"
!define VERSIONMAJOR 1
!define VERSIONMINOR 0
!define VERSIONBUILD 0

RequestExecutionLevel admin
InstallDir "$PROGRAMFILES64\${COMPANYNAME}\${APPNAME}"

Page directory
Page instfiles

Section "TaskbarEqualizer"
    SetOutPath $INSTDIR
    File /r "publish\TaskbarEqualizer\*"
    
    ; Create shortcuts
    CreateDirectory "$SMPROGRAMS\${APPNAME}"
    CreateShortCut "$SMPROGRAMS\${APPNAME}\${APPNAME}.lnk" "$INSTDIR\TaskbarEqualizer.exe"
    CreateShortCut "$DESKTOP\${APPNAME}.lnk" "$INSTDIR\TaskbarEqualizer.exe"
    
    ; Registry
    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPNAME}" "DisplayName" "${APPNAME}"
    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPNAME}" "UninstallString" "$INSTDIR\uninstall.exe"
    WriteUninstaller "$INSTDIR\uninstall.exe"
SectionEnd

Section "Uninstall"
    Delete "$INSTDIR\*"
    RMDir "$INSTDIR"
    Delete "$SMPROGRAMS\${APPNAME}\${APPNAME}.lnk"
    Delete "$DESKTOP\${APPNAME}.lnk"
    RMDir "$SMPROGRAMS\${APPNAME}"
    DeleteRegKey HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPNAME}"
SectionEnd
```

### **3. Build NSIS Installer**
```powershell
# Compile the installer
makensis TaskbarEqualizer.nsi
```

## **Option 3: Windows Package Manager (winget)**

### **1. Create Manifest**
```yaml
# TaskbarEqualizer.yaml
PackageIdentifier: Graphlight.TaskbarEqualizer
PackageVersion: 1.0.0
PackageName: TaskbarEqualizer
Publisher: Graphlight
License: MIT
ShortDescription: Professional Audio Visualizer
Installers:
- Architecture: x64
  InstallerType: exe
  InstallerUrl: https://github.com/YourRepo/releases/download/v1.0.0/TaskbarEqualizerSetup.exe
  InstallerSha256: [SHA256_HASH]
ManifestType: singleton
ManifestVersion: 1.0.0
```

## **Current Fastest Option: Self-Contained EXE**

Let me create this for you right now: