@echo off
cd /d "%~dp0"
if not exist "tools\ffmpeg.exe" goto setup
if not exist "tools\ffprobe.exe" goto setup
goto launch
:setup
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Setup-FFmpeg.ps1"
if errorlevel 1 (
 echo FFmpeg setup failed. Please check your network connection and try again.
 pause
 exit /b 1
)
:launch
if not exist "ClipDock.exe" (
 echo ClipDock.exe is missing. Please download and extract the release ZIP.
 pause
 exit /b 1
)
start "" "%~dp0ClipDock.exe"
