@echo off
rem Start the Api and the Client in two windows.
call "%~dp0_common.bat"
start "PharmacyWarehouse Api" cmd /k dotnet run --project src\PharmacyWarehouse.Api
start "PharmacyWarehouse Client" cmd /k dotnet run --project src\PharmacyWarehouse.Client
