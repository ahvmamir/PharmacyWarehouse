@echo off
rem Run all tests. Optional: give part of a test class name to run only those, for example: run-tests StocktakePeriodTests
call "%~dp0_common.bat"
if "%~1"=="" (
  dotnet test PharmacyWarehouse.sln
) else (
  dotnet test PharmacyWarehouse.sln --filter "FullyQualifiedName~%~1"
)
pause
