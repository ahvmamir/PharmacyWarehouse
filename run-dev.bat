@echo off
cd /d "%~dp0"
start "PharmacyWarehouse Api" cmd /k dotnet run --project src\PharmacyWarehouse.Api
start "PharmacyWarehouse Client" cmd /k dotnet run --project src\PharmacyWarehouse.Client