@echo off
rem Fill the development database with 100 clearly fake test items. Only on a database without test items.
call "%~dp0_common.bat"
dotnet run --project src\PharmacyWarehouse.Api -- seed-test-data
pause
