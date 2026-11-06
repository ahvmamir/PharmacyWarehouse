@echo off
rem Reset the password of an existing user. The program asks for the new password.
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
title PharmacyWarehouse - reset password
set "APP=C:\PharmacyWarehouse\app"
if exist "%APP%\PharmacyWarehouse.Api.exe" goto haveapp
echo The program is not installed in %APP%. Run 1-install.bat first.
pause
exit /b 1

:haveapp
:askname
set "USER_NAME="
set /p "USER_NAME=Username whose password is reset: "
if not defined USER_NAME goto askname

pushd "%APP%"
"PharmacyWarehouse.Api.exe" reset-password "%USER_NAME%"
set "RC=%errorlevel%"
popd

if not "%RC%"=="0" goto fail
echo.
echo The password of "%USER_NAME%" was reset. Give the new password to the user in a safe way.
pause
exit /b 0

:fail
echo.
echo Resetting the password failed (exit code %RC%). Read the message above and try again.
pause
exit /b 1
