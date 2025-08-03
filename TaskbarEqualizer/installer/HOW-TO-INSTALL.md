# 🚀 How to Install TaskbarEqualizer

## **✨ Quick Install (Recommended)**

### **Option 1: Simple Installer (Double-click)**
1. **Right-click** `Install TaskbarEqualizer.bat`
2. Select **"Run as administrator"**  
3. Follow the prompts
4. Click **"Launch TaskbarEqualizer"** when installation completes

**That's it!** 🎉

---

## **🔧 Advanced Installation Options**

### **Option 2: PowerShell Installer**
```powershell
# Run as Administrator
.\install-taskbar-equalizer.ps1

# With custom options
.\install-taskbar-equalizer.ps1 -InstallPath "C:\MyApps\TaskbarEqualizer" -EnableAutoStart
```

### **Option 3: Manual Installation**
1. Copy `publish\TaskbarEqualizer\TaskbarEqualizer.exe` to your desired location
2. Create shortcuts manually
3. Run `TaskbarEqualizer.exe`

---

## **📋 Installation Details**

### **What Gets Installed:**
- ✅ **Main Application**: `TaskbarEqualizer.exe` (164MB self-contained)
- ✅ **Desktop Shortcut**: Quick access from desktop
- ✅ **Start Menu Entry**: Professional integration
- ✅ **Uninstaller**: Clean removal support
- ✅ **Registry Entries**: Proper Windows integration
- ✅ **Settings Directory**: `%APPDATA%\TaskbarEqualizer`

### **Installation Locations:**
- **Program Files**: `C:\Program Files\TaskbarEqualizer\`
- **Settings**: `C:\Users\[Username]\AppData\Roaming\TaskbarEqualizer\`
- **Shortcuts**: Desktop and Start Menu

### **System Requirements:**
- ✅ **Windows 10** version 1903 or later
- ✅ **Windows 11** (recommended)
- ✅ **Administrator privileges** (for installation only)
- ✅ **164MB** free disk space

---

## **🎵 After Installation**

### **Launch TaskbarEqualizer:**
1. **Desktop**: Double-click the TaskbarEqualizer shortcut
2. **Start Menu**: Search for "TaskbarEqualizer"
3. **Direct**: Run `C:\Program Files\TaskbarEqualizer\TaskbarEqualizer.exe`

### **First Launch:**
- Application appears in system tray
- Right-click tray icon for settings
- Audio visualization starts automatically
- Settings saved to your user profile

---

## **⚙️ Configuration Options**

### **Auto-Start with Windows:**
```powershell
# Enable during installation
.\install-taskbar-equalizer.ps1 -EnableAutoStart

# Or configure manually via context menu
```

### **Custom Installation Path:**
```powershell
.\install-taskbar-equalizer.ps1 -InstallPath "D:\Applications\TaskbarEqualizer"
```

### **Silent Installation:**
```powershell
.\install-taskbar-equalizer.ps1 -Silent -EnableAutoStart
```

---

## **🗑️ Uninstallation**

### **Method 1: Control Panel**
1. **Control Panel** → **Programs** → **Programs and Features**
2. Find **"TaskbarEqualizer"**
3. Click **"Uninstall"**

### **Method 2: Start Menu**
1. **Start Menu** → **TaskbarEqualizer** → **"Uninstall TaskbarEqualizer"**

### **Method 3: Direct**
```powershell
# Run the uninstaller directly
"C:\Program Files\TaskbarEqualizer\uninstall.ps1"
```

**Complete Removal**: All files, shortcuts, and registry entries are cleaned up automatically.

---

## **🔧 Troubleshooting**

### **"Access Denied" Error**
- **Solution**: Right-click installer → **"Run as administrator"**

### **"Execution Policy" Error**
```powershell
# Fix PowerShell execution policy
Set-ExecutionPolicy -ExecutionPolicy RemoteSigned -Scope CurrentUser
```

### **Application Won't Start**
1. Check Windows version (requires Windows 10 1903+)
2. Verify installation completed successfully
3. Check Windows Event Logs for errors

### **Missing Audio Visualization**
1. Ensure audio is playing on your system
2. Check audio device settings
3. Right-click tray icon → Settings → Audio Device

### **Performance Issues**
1. Close unnecessary applications
2. Check Task Manager for CPU usage
3. Adjust visualization quality in settings

---

## **🆘 Support**

### **Getting Help:**
1. **Check logs**: `%APPDATA%\TaskbarEqualizer\logs\`
2. **GitHub Issues**: Create an issue with logs attached
3. **Documentation**: See `README.md` for detailed information

### **Common File Locations:**
- **Application**: `C:\Program Files\TaskbarEqualizer\`
- **Settings**: `%APPDATA%\TaskbarEqualizer\settings.json`
- **Logs**: `%APPDATA%\TaskbarEqualizer\logs\`
- **Uninstaller**: `C:\Program Files\TaskbarEqualizer\uninstall.ps1`

---

## **🎯 Quick Reference**

| Action | Command |
|--------|---------|
| **Install** | Right-click `Install TaskbarEqualizer.bat` → Run as administrator |
| **Launch** | Desktop shortcut or Start Menu |
| **Settings** | Right-click system tray icon |
| **Uninstall** | Control Panel → Programs → TaskbarEqualizer |
| **Logs** | `%APPDATA%\TaskbarEqualizer\logs\` |

---

**Happy visualizing! 🎵✨**