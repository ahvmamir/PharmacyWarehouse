@echo off
rem Check that every batch stock equals the sum of its movements. Reports only, changes nothing.
call "%~dp0_common.bat"
dotnet run --project src\PharmacyWarehouse.Api -- verify-stock
pause
