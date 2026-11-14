@echo off
if not exist "%~dp0artifacts\Facet\Facet.Launcher.exe" (
  echo Facet is not built yet. Run: powershell -NoProfile -ExecutionPolicy Bypass -File scripts\build.ps1
  pause
  exit /b 1
)
start "" "%~dp0artifacts\Facet\Facet.Launcher.exe" %*
