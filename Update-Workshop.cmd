@echo off
setlocal
set "advisorPwsh=pwsh.exe"
if exist "%ProgramFiles%\PowerShell\7\pwsh.exe" set "advisorPwsh=%ProgramFiles%\PowerShell\7\pwsh.exe"
"%advisorPwsh%" -NoProfile -ExecutionPolicy Bypass -File "%~dp0Local-WorkshopUpdate.ps1" -Publish
set "result=%errorlevel%"
pause
exit /b %result%
