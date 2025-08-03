# TaskbarEqualizer Installer

Professional Windows installer (MSI) for TaskbarEqualizer using WiX Toolset.

## 🎯 **Quick Start**

### **Option 1: Automated Build (Recommended)**

```powershell
# Run the build script
.\build-installer.ps1

# Or with options
.\build-installer.ps1 -Configuration Release -Clean -Verbose
```

### **Option 2: Manual Build**

```powershell
# 1. Build the application first
cd J:\Graphlight\TaskbarEqualizer
dotnet build --configuration Release

# 2. Build the installer
cd installer
dotnet build TaskbarEqualizer.Installer\TaskbarEqualizer.Installer.wixproj
```

## 📋 **Prerequisites**

### **Required Software:**
1. **WiX Toolset v3.11+**
   - Download: https://github.com/wixtoolset/wix3/releases
   - Add to PATH: `C:\Program Files (x86)\WiX Toolset v3.11\bin`

2. **.NET 8.0 SDK**
   - Download: https://dotnet.microsoft.com/download/dotnet/8.0

3. **Visual Studio 2022** (or VS Build Tools)
   - With ".NET desktop development" workload

### **Verification:**
```powershell
# Check WiX installation
candle.exe -?

# Check .NET SDK
dotnet --version

# Check MSBuild
dotnet build --help
```

## 🏗️ **Installer Features**

### **Installation Components:**
- ✅ Main TaskbarEqualizer application
- ✅ All required libraries and dependencies
- ✅ Desktop and Start Menu shortcuts
- ✅ Windows Registry integration
- ✅ Auto-start configuration (optional)
- ✅ File associations for settings files
- ✅ Proper uninstall support

### **User Experience:**
- 🎨 Professional Windows 11 styled UI
- 🖱️ Simple wizard-based installation
- 📁 Customizable installation directory
- 🚀 Optional "Launch after install"
- 📄 License agreement display
- 🔧 Feature selection dialog

### **Technical Features:**
- 📦 Single MSI file (all dependencies included)
- 🔄 Upgrade support (preserves settings)
- 🗑️ Clean uninstallation
- 📊 Windows Installer logging
- 🛡️ Administrator privileges handled automatically
- 🔐 Code signing ready (certificates not included)

## 📁 **File Structure**

```
installer/
├── TaskbarEqualizer.Installer/
│   ├── TaskbarEqualizer.Installer.wixproj  # WiX project file
│   ├── Product.wxs                         # Main product definition
│   ├── Components.wxs                      # Application components
│   ├── UI.wxs                             # Custom user interface
│   ├── License.rtf                        # License agreement text
│   └── Resources/                         # Images and resources
│       ├── Banner.bmp                     # Installer banner (493×58)
│       ├── Dialog.bmp                     # Dialog background (493×312)
│       └── Icon.ico                       # Application icon
├── build-installer.ps1                    # Automated build script
├── README.md                             # This file
└── output/                               # Generated installer files
    ├── TaskbarEqualizerSetup.msi         # Final installer
    └── checksums.txt                     # File verification
```

## 🚀 **Build Process**

### **Step 1: Application Build**
```powershell
dotnet build --configuration Release --runtime win-x64
```

### **Step 2: Installer Generation**
```powershell
# WiX compiles .wxs files to .wixobj
candle.exe Product.wxs Components.wxs UI.wxs

# WiX links .wixobj files to create .msi
light.exe Product.wixobj Components.wixobj UI.wixobj -out TaskbarEqualizerSetup.msi
```

### **Step 3: Verification**
```powershell
# Test installer
msiexec /i TaskbarEqualizerSetup.msi /l*v install.log

# Verify installation
Get-WmiObject -Class Win32_Product | Where-Object {$_.Name -like "*TaskbarEqualizer*"}
```

## 🎛️ **Customization Options**

### **Build Script Parameters:**
```powershell
.\build-installer.ps1 [OPTIONS]

OPTIONS:
  -Configuration  Build configuration (Debug/Release)
  -Platform       Target platform (x64/x86/AnyCPU) 
  -OutputPath     Output directory for installer
  -Clean          Clean previous builds first
  -Verbose        Enable verbose logging
```

### **Installer Properties:**
Edit `Product.wxs` to customize:
```xml
<!-- Product Information -->
<Product Name="TaskbarEqualizer" 
         Manufacturer="Graphlight"
         Version="1.0.0.0" />

<!-- Installation Directory -->
<Directory Id="INSTALLFOLDER" Name="TaskbarEqualizer" />

<!-- Features -->
<Feature Id="ProductFeature" Title="TaskbarEqualizer" Level="1">
  <ComponentGroupRef Id="ProductComponents" />
</Feature>
```

## 🧪 **Testing**

### **Installation Testing:**
```powershell
# Silent install
msiexec /i TaskbarEqualizerSetup.msi /quiet

# Install with logging
msiexec /i TaskbarEqualizerSetup.msi /l*v install.log

# Repair installation
msiexec /fa TaskbarEqualizerSetup.msi

# Uninstall
msiexec /x TaskbarEqualizerSetup.msi /quiet
```

### **Validation Checklist:**
- [ ] Application launches after installation
- [ ] Start Menu shortcut works
- [ ] Desktop shortcut works (if selected)
- [ ] Auto-start registry entry created (if selected)
- [ ] Settings directory created
- [ ] All DLL dependencies included
- [ ] Uninstall removes all files and registry entries
- [ ] Upgrade preserves user settings

## 🔧 **Troubleshooting**

### **Common Issues:**

**1. "candle.exe not found"**
```powershell
# Solution: Add WiX to PATH
$env:PATH += ";C:\Program Files (x86)\WiX Toolset v3.11\bin"
```

**2. "Application files not found"**
```powershell
# Solution: Build application first
cd J:\Graphlight\TaskbarEqualizer
dotnet build --configuration Release
```

**3. "MSBuild not found"**
```powershell
# Solution: Use Developer Command Prompt or install VS Build Tools
# Or use dotnet build instead of MSBuild directly
```

**4. "Permission denied during install"**
```powershell
# Solution: Run installer as Administrator
# Right-click → "Run as administrator"
```

### **Debugging:**
```powershell
# Enable MSI logging
msiexec /i TaskbarEqualizerSetup.msi /l*v debug.log

# View WiX build output
dotnet build -verbosity diagnostic

# Test installer without actually installing
msiexec /a TaskbarEqualizerSetup.msi TARGETDIR=C:\ExtractedMSI
```

## 📋 **Advanced Configuration**

### **Code Signing:**
```powershell
# Sign the installer (requires certificate)
signtool sign /f certificate.pfx /p password /t http://timestamp.digicert.com TaskbarEqualizerSetup.msi
```

### **Custom Actions:**
Add to `Product.wxs`:
```xml
<CustomAction Id="CustomAction" 
              BinaryKey="CustomActionDLL" 
              DllEntry="CustomActionFunction" />
```

### **Registry Modifications:**
Add to `Components.wxs`:
```xml
<RegistryKey Root="HKLM" Key="SOFTWARE\MyCompany\MyApp">
  <RegistryValue Type="string" Name="InstallPath" Value="[INSTALLFOLDER]" />
</RegistryKey>
```

## 🎉 **Deployment**

### **Distribution Options:**
1. **Direct Download:** Host the MSI file on your website
2. **Package Managers:** Submit to Chocolatey, winget, etc.
3. **Enterprise:** Deploy via Group Policy or SCCM
4. **Auto-Update:** Integrate with deployment tools

### **Release Checklist:**
- [ ] Version number updated in all files
- [ ] License text reviewed and updated
- [ ] All dependencies included and tested
- [ ] Installer tested on clean Windows machines
- [ ] Code signed (for public distribution)
- [ ] Documentation updated
- [ ] Changelog updated

---

## 📞 **Support**

For installer issues:
1. Check the troubleshooting section above
2. Review build logs and MSI installation logs
3. Create an issue on GitHub with logs attached

**Happy installing! 🚀**