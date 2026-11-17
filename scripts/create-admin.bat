@echo off
rem Create an admin user in the development database. The Api must be closed. The password is typed hidden.
call "%~dp0_common.bat"
set /p USERNAME_NEW=Admin username: 
if "%USERNAME_NEW%"=="" exit /b 1
dotnet run --project src\PharmacyWarehouse.Api -- create-admin %USERNAME_NEW%
pause
