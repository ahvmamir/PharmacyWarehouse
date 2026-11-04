@echo off
rem Build the Windows install package: publish\Emu-package.zip
rem Contents: app folder (self-contained, no .NET needed on the VM) plus install, update, uninstall scripts.
rem Runs all tests first; a failing test stops the build.
rem The window waits for a key at the end, so a double-click does not hide the result.
rem Option: publish-package.bat noprecompile
rem   skips ReadyToRun precompilation; use it when nuget.org cannot be reached for the extra
rem   precompiler package. The service then starts a little slower after a restart.
cd /d "%~dp0"

set R2R=true
if /i "%~1"=="noprecompile" set R2R=false

dotnet test Emu.sln
if errorlevel 1 goto fail

if exist publish\Emu-package rmdir /s /q publish\Emu-package
if exist publish\Emu-package.zip del /q publish\Emu-package.zip

dotnet publish src\Emu.Api -c Release -p:EmuPackage=true -p:PublishReadyToRun=%R2R% -o publish\Emu-package\app
if errorlevel 1 goto fail

rem Never ship a database or settings file from the build machine.
del /q publish\Emu-package\app\*.db 2>nul
del /q publish\Emu-package\app\appsettings.Development.json 2>nul
del /q publish\Emu-package\app\appsettings.Production.json 2>nul

copy /y deploy\*.ps1 publish\Emu-package\ >nul
if errorlevel 1 goto fail

powershell -NoProfile -Command "Compress-Archive -Path publish\Emu-package\* -DestinationPath publish\Emu-package.zip"
if errorlevel 1 goto fail

echo.
echo Package ready: "%cd%\publish\Emu-package.zip"
echo Copy it to the VM, extract, then run install.ps1 or update.ps1 in an admin PowerShell.
pause
exit /b 0

:fail
echo.
echo Build failed. Nothing was packaged. Read the first error above.
echo If the error is NU1301 (nuget.org not reachable), try again, or run: publish-package.bat noprecompile
echo If the Api or Client window of run-dev.bat is open, close it first: it locks the build files.
pause
exit /b 1
