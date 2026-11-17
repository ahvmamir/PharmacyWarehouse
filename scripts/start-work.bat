@echo off
rem Start of a work session on this PC: get the latest code, update the database, build.
call "%~dp0_common.bat"
git pull
if errorlevel 1 (
  echo git pull failed. Check git status, then try again.
  pause
  exit /b 1
)
call "%~dp0db-update.bat"
if errorlevel 1 exit /b 1
call "%~dp0_common.bat"
dotnet build PharmacyWarehouse.sln
if errorlevel 1 (
  echo Build FAILED.
  pause
  exit /b 1
)
echo Ready. Run scripts\run-dev.bat to start the Api and the Client.
pause
