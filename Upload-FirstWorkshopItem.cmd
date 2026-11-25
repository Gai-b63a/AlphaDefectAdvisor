@echo off
setlocal
pushd "%~dp0" || exit /b 1
set "advisorPwsh=pwsh.exe"
if exist "%ProgramFiles%\PowerShell\7\pwsh.exe" set "advisorPwsh=%ProgramFiles%\PowerShell\7\pwsh.exe"
"%advisorPwsh%" -NoProfile -ExecutionPolicy Bypass -File "%~dp0Upload-FirstWorkshopItem.ps1"
set "result=%errorlevel%"
if "%result%"=="9009" echo PowerShell 7 is required: https://aka.ms/powershell-release?tag=stable
if not "%result%"=="0" echo Upload did not finish. Check the message above before retrying.
popd
pause
exit /b %result%
