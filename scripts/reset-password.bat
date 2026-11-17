@echo off
rem Set a new password for a user in the development database. The Api must be closed. The password is typed hidden.
call "%~dp0_common.bat"
set /p USER_NAME=Username: 
if "%USER_NAME%"=="" exit /b 1
dotnet run --project src\PharmacyWarehouse.Api -- reset-password %USER_NAME%
pause
