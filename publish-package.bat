@echo off
rem Build the Windows install package: publish\PharmacyWarehouse-package.zip
rem Contents: app folder (self-contained, no .NET needed on the server PC) plus install, update, uninstall scripts.
rem Runs all tests first; a failing test stops the build.
rem The window waits for a key at the end, so a double-click does not hide the result.
rem Option: publish-package.bat noprecompile
rem   skips ReadyToRun precompilation; use it when nuget.org cannot be reached for the extra
rem   precompiler package. The service then starts a little slower after a restart.
cd /d "%~dp0"

set R2R=true
if /i "%~1"=="noprecompile" set R2R=false

@dotnet test PharmacyWarehouse.sln
@if errorlevel 1 goto fail

if exist publish\PharmacyWarehouse-package rmdir /s /q publish\PharmacyWarehouse-package
if exist publish\PharmacyWarehouse-package.zip del /q publish\PharmacyWarehouse-package.zip

dotnet publish src\PharmacyWarehouse.Api -c Release -p:WarehousePackage=true -p:WarehousePrecompile=%R2R% -o publish\PharmacyWarehouse-package\app
if errorlevel 1 goto fail

rem Never ship a database or settings file from the build machine.
del /q publish\PharmacyWarehouse-package\app\*.db 2>nul
del /q publish\PharmacyWarehouse-package\app\appsettings.Development.json 2>nul
del /q publish\PharmacyWarehouse-package\app\appsettings.Production.json 2>nul

copy /y deploy\*.ps1 publish\PharmacyWarehouse-package\ >nul
if errorlevel 1 goto fail

powershell -NoProfile -Command "Compress-Archive -Path publish\PharmacyWarehouse-package\* -DestinationPath publish\PharmacyWarehouse-package.zip"
if errorlevel 1 goto fail

echo.
echo Package ready: "%cd%\publish\PharmacyWarehouse-package.zip"
echo Copy it to the server PC, extract, then run install.ps1 or update.ps1 in an admin PowerShell.
pause
exit /b 0

:fail
echo.
echo Build failed. Nothing was packaged. Read the first error above.
echo If the error is NU1301 (nuget.org not reachable), try again, or run: publish-package.bat noprecompile
echo If the Api or Client window of run-dev.bat is open, close it first: it locks the build files.
pause
exit /b 1
