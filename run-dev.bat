@echo off
cd /d "%~dp0"
start "Emu Api" cmd /k dotnet run --project src\Emu.Api
start "Emu Client" cmd /k dotnet run --project src\Emu.Client