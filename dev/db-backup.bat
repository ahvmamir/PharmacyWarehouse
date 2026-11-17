@echo off
rem Copy the development database into the backups folder (ignored by Git).
call "%~dp0_common.bat"
set DB=src\PharmacyWarehouse.Api\pharmacywarehouse.dev.db
if not exist "%DB%" (
  echo No development database yet. Nothing to back up.
  exit /b 0
)
tasklist /fi "imagename eq PharmacyWarehouse.Api.exe" /v | findstr /i "Console" >nul
if not errorlevel 1 (
  echo The development Api seems to be running. Close it first, then run this again.
  pause
  exit /b 1
)
if not exist backups mkdir backups
for /f %%t in ('powershell -NoProfile -Command "Get-Date -Format yyyyMMdd-HHmmss"') do set STAMP=%%t
copy /y "%DB%" "backups\dev-%STAMP%.db" >nul
if errorlevel 1 (
  echo Backup failed.
  exit /b 1
)
echo Backup saved: backups\dev-%STAMP%.db
exit /b 0
