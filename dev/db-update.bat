@echo off
rem Back up the development database, then apply all new migrations.
call "%~dp0_common.bat"
call "%~dp0db-backup.bat"
if errorlevel 1 exit /b 1
call "%~dp0_common.bat"
dotnet tool restore
dotnet ef database update --project src\PharmacyWarehouse.Api
if errorlevel 1 (
  echo Database update FAILED. Restore the newest file from the backups folder if needed.
  pause
  exit /b 1
)
echo Database is up to date.
