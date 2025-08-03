@echo off
title TaskbarEqualizer Installer

echo.
echo  ===============================================
echo   TaskbarEqualizer - Simple Installer
echo  ===============================================
echo   Professional Audio Visualizer for Windows
echo  ===============================================
echo.

:: Check for administrator privileges
net session >nul 2>&1
if %errorLevel% == 0 (
    echo [OK] Running as Administrator
) else (
    echo [ERROR] This installer requires administrator privileges.
    echo.
    echo Please right-click this file and select "Run as administrator"
    echo.
    pause
    exit /b 1
)

echo.
echo Starting PowerShell installer...
echo.

:: Run the PowerShell installer
powershell.exe -ExecutionPolicy Bypass -File "%~dp0install-taskbar-equalizer-clean.ps1"

if %errorLevel% == 0 (
    echo.
    echo Installation completed successfully!
) else (
    echo.
    echo Installation failed. Please check the error messages above.
)

echo.
pause