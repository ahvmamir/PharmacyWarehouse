@echo off
rem Update an existing PharmacyWarehouse install. Run it from the extracted NEW package folder (the one with the app folder).
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
title PharmacyWarehouse - update
if exist "%~dp0app\PharmacyWarehouse.Api.exe" goto haveapp
echo The app folder was not found next to this file.
echo Extract the NEW package and run update.bat from that folder, not from the old install.
pause
exit /b 1

:haveapp
echo Unblocking package files (only needed once)...
powershell -NoProfile -Command "Get-ChildItem -Path '%~dp0' -Recurse | Unblock-File"

echo.
echo Updating PharmacyWarehouse. A database backup is made first.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0update.ps1"
if errorlevel 1 goto fail

echo.
echo Update finished. Check the health line above.
pause
exit /b 0

:fail
echo.
echo The update stopped with an error. Read the first error above.
echo The backup made before the update is in the pre-update folder inside the data folder
echo (default: C:\PharmacyWarehouseData\pre-update). The rollback steps are in docs\deploy.md.
pause
exit /b 1
