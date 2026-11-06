@echo off
rem Create a new admin user in the installed PharmacyWarehouse. The program asks for the password.
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
title PharmacyWarehouse - create admin
set "APP=C:\PharmacyWarehouse\app"
if exist "%APP%\PharmacyWarehouse.Api.exe" goto haveapp
echo The program is not installed in %APP%. Run 1-install.bat first.
pause
exit /b 1

:haveapp
:askname
set "ADMIN_NAME="
set /p "ADMIN_NAME=Username of the new admin: "
if not defined ADMIN_NAME goto askname

pushd "%APP%"
"PharmacyWarehouse.Api.exe" create-admin "%ADMIN_NAME%"
set "RC=%errorlevel%"
popd

if not "%RC%"=="0" goto fail
echo.
echo Admin "%ADMIN_NAME%" was created.
pause
exit /b 0

:fail
echo.
echo Creating the admin failed (exit code %RC%). Read the message above and try again.
pause
exit /b 1
