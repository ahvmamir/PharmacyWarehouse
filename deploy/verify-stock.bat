@echo off
rem Check the stock data of the installed program (runs the program command verify-stock). Changes nothing.
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
title PharmacyWarehouse - verify stock
set "APP=C:\PharmacyWarehouse\app"
if exist "%APP%\PharmacyWarehouse.Api.exe" goto haveapp
echo The program is not installed in %APP%. Run 1-install.bat first.
pause
exit /b 1

:haveapp
pushd "%APP%"
"PharmacyWarehouse.Api.exe" verify-stock
set "RC=%errorlevel%"
popd

echo.
if not "%RC%"=="0" goto fail
echo Stock check finished without errors.
pause
exit /b 0

:fail
echo The stock check reported a problem (exit code %RC%). Read the messages above.
pause
exit /b 1
