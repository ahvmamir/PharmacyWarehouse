@echo off
rem First-time install of PharmacyWarehouse (run once on the server PC).
rem Creates the local CA (only if missing), the server certificate, then installs the service.
cd /d "%~dp0"
net session >nul 2>&1
if errorlevel 1 goto elevate
goto run

:elevate
echo Administrator rights are required. A new window will open and ask for permission.
powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
if errorlevel 1 goto elevfail
exit /b 0

:elevfail
echo Could not get administrator rights. Right-click this file and choose Run as administrator.
pause
exit /b 1

:run
title PharmacyWarehouse - first-time install
echo ============================================
echo PharmacyWarehouse first-time install
echo ============================================
echo.
echo Unblocking package files (only needed once)...
powershell -NoProfile -Command "Get-ChildItem -Path '%~dp0' -Recurse | Unblock-File"

set "DETECTED_IP="
for /f "usebackq delims=" %%I in (`powershell -NoProfile -Command "Get-NetIPConfiguration | Where-Object { $_.IPv4DefaultGateway -ne $null } | Select-Object -First 1 | Select-Object -ExpandProperty IPv4Address | Select-Object -First 1 -ExpandProperty IPAddress"`) do set "DETECTED_IP=%%I"

set "IP=%DETECTED_IP%"
if defined DETECTED_IP echo Detected IP address of this PC: %DETECTED_IP%
if not defined DETECTED_IP echo Could not detect the IP address automatically. Please type it.

:askip
set "ANSWER="
set /p "ANSWER=Server IP address (Enter to use the shown value): "
if defined ANSWER set "IP=%ANSWER%"
if not defined IP goto askip
powershell -NoProfile -Command "if ('%IP%' -match '^\d{1,3}(\.\d{1,3}){3}$') { exit 0 } exit 1"
if errorlevel 1 (
  echo "%IP%" is not an IPv4 address like 192.168.1.20. Try again.
  set "IP="
  goto askip
)
echo Using IP address: %IP%
echo.

:askadmin
set "ADMIN="
set /p "ADMIN=Username of the first admin (required): "
if not defined ADMIN goto askadmin
echo.

set "CERT_DIR=%PUBLIC%\Desktop\Darubar-Certificates"
if not exist "%CERT_DIR%" mkdir "%CERT_DIR%"
if not exist "%CERT_DIR%" goto fail

if exist "%CERT_DIR%\pharmacywarehouse-root.pfx" goto haveroot

echo Creating the local certificate authority (CA) and the server certificate.
echo You will be asked for two passwords. Write them down: the CA password is needed to renew the certificate.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0setup-local-ca.ps1" -OutDir "%CERT_DIR%" -IpAddress "%IP%"
if errorlevel 1 goto fail
goto install

:haveroot
echo An existing CA was found in %CERT_DIR%. It will be reused.
if exist "%CERT_DIR%\pharmacywarehouse-server.pfx" goto install
echo Creating a new server certificate from the existing CA. You will be asked for the CA password.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0setup-local-ca.ps1" -OutDir "%CERT_DIR%" -IpAddress "%IP%" -RootPfx "%CERT_DIR%\pharmacywarehouse-root.pfx"
if errorlevel 1 goto fail

:install
if not exist "%CERT_DIR%\pharmacywarehouse-server.pfx" goto fail
echo.
echo Installing the PharmacyWarehouse service...
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0install.ps1" -PfxPath "%CERT_DIR%\pharmacywarehouse-server.pfx" -IpAddress "%IP%" -AdminUser "%ADMIN%"
if errorlevel 1 goto fail

del /q "%CERT_DIR%\pharmacywarehouse-server.pfx"

echo.
echo ============================================
echo Install finished.
echo ============================================
echo 1. On every device, install pharmacywarehouse-root.cer (Windows, iPhone)
echo    or pharmacywarehouse-root.crt (Android) from the folder:
echo    %CERT_DIR%
echo 2. Move pharmacywarehouse-root.pfx to a USB stick and keep it safe.
echo    Do not keep it on this PC or install it on any device.
echo 3. Open https://%IP%/ in a browser on the devices.
echo.
pause
exit /b 0

:fail
echo.
echo The install stopped at a step above. Read the error message, fix the cause and run this file again.
echo The certificate folder is: %CERT_DIR%
pause
exit /b 1
