@echo off
rem Remove the PharmacyWarehouse service, firewall rule, backup task and program folder. The data folder is kept.
cd /d "%~dp0"
net session >nul 2>&1
if errorlevel 1 goto elevate
goto run

:elevate
echo Administrator rights are required. A new window will open and ask for permission.
powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
if errorlevel 1 goto elevfail
exit /b 0

:elevfail
echo Could not get administrator rights. Right-click this file and choose Run as administrator.
pause
exit /b 1

:run
title PharmacyWarehouse - uninstall
echo ============================================
echo This removes the PharmacyWarehouse service, its firewall rule,
echo the nightly backup task and the program folder C:\PharmacyWarehouse\app.
echo The data folder (database, certificate, keys, backups) is KEPT.
echo ============================================
echo.
set "CONFIRM="
set /p "CONFIRM=Type YES in capital letters to continue, anything else cancels: "
if not "%CONFIRM%"=="YES" goto cancel

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0uninstall.ps1" -Confirm:$false
if errorlevel 1 goto fail

echo.
echo PharmacyWarehouse was removed. The data folder was kept.
pause
exit /b 0

:cancel
echo.
echo Cancelled. Nothing was changed.
pause
exit /b 0

:fail
echo.
echo The uninstall stopped with an error. Read the message above.
pause
exit /b 1
