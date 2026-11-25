@echo off
pwsh.exe -NoProfile -File "%~dp0Start-LocalGame.ps1" %*
if errorlevel 1 pause
