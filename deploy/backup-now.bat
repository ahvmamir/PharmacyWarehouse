@echo off
rem Make a backup of the database now, into the folder you choose.
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
title PharmacyWarehouse - backup now
set "APP=C:\PharmacyWarehouse\app"
if exist "%APP%\PharmacyWarehouse.Api.exe" goto haveapp
echo The program is not installed in %APP%. Run 1-install.bat first.
pause
exit /b 1

:haveapp
set "DEST=%PUBLIC%\Documents\Darubar-Backups"
set "ANSWER="
echo Default backup folder: %DEST%
echo A backup on this PC does not protect against a failure of this PC.
echo Copy the backups to another PC or a USB stick as well.
set /p "ANSWER=Backup folder (Enter to use the default): "
if defined ANSWER set "DEST=%ANSWER%"

if not exist "%DEST%" mkdir "%DEST%"
if not exist "%DEST%" goto fail

pushd "%APP%"
"PharmacyWarehouse.Api.exe" backup "%DEST%"
set "RC=%errorlevel%"
popd

if not "%RC%"=="0" goto fail
echo.
echo Backup finished. Folder: %DEST%
pause
exit /b 0

:fail
echo.
echo The backup failed (exit code %RC%) or the folder could not be created: %DEST%
echo Read the message above and try again.
pause
exit /b 1
