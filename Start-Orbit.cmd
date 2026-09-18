@echo off
cd /d "%~dp0"
if not exist "release\Orbit\Orbit.exe" (
  powershell.exe -NoProfile -ExecutionPolicy Bypass -File "scripts\build.ps1"
  if errorlevel 1 (pause & exit /b 1)
)
start "" "release\Orbit\Orbit.exe" "%cd%"
