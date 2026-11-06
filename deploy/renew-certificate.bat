@echo off
rem Renew the HTTPS certificate of the installed server with the SAME local CA. Devices need no change.
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
title PharmacyWarehouse - renew certificate
echo ============================================
echo Renew the server certificate with the existing CA
echo ============================================
echo.
powershell -NoProfile -Command "Unblock-File -Path '%~dp0setup-local-ca.ps1','%~dp0apply-certificate.ps1'"

set "DETECTED_IP="
for /f "usebackq delims=" %%I in (`powershell -NoProfile -Command "Get-NetIPConfiguration | Where-Object { $_.IPv4DefaultGateway -ne $null } | Select-Object -First 1 | Select-Object -ExpandProperty IPv4Address | Select-Object -First 1 -ExpandProperty IPAddress"`) do set "DETECTED_IP=%%I"

set "IP=%DETECTED_IP%"
if defined DETECTED_IP echo Detected IP address of this PC: %DETECTED_IP%

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

set "ROOT=%PUBLIC%\Desktop\Darubar-Certificates\pharmacywarehouse-root.pfx"
echo Default CA file: %ROOT%
set "ANSWER="
set /p "ANSWER=Path of the CA file pharmacywarehouse-root.pfx (Enter for the default): "
if defined ANSWER set "ROOT=%ANSWER%"
if not exist "%ROOT%" goto noroot

for %%F in ("%ROOT%") do set "CERT_DIR=%%~dpF"
set "CERT_DIR=%CERT_DIR:~0,-1%"
set "SERVER=%CERT_DIR%\pharmacywarehouse-server.pfx"

echo.
echo Step 1 of 2: creating the new server certificate. You will be asked for the CA password
echo and for a password for the new server file.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0setup-local-ca.ps1" -RootPfx "%ROOT%" -OutDir "%CERT_DIR%" -IpAddress "%IP%"
if errorlevel 1 goto fail
if not exist "%SERVER%" goto fail

echo.
echo Step 2 of 2: installing the new certificate on the server. Enter the password of the server file.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0apply-certificate.ps1" -PfxPath "%SERVER%" -AddHost "%IP%"
if errorlevel 1 goto fail

del /q "%SERVER%"

echo.
echo ============================================
echo Certificate renewed. The server file was deleted.
echo ============================================
echo Devices need no change, because they trust the CA and not the server certificate.
echo.
pause
exit /b 0

:noroot
echo The CA file was not found: %ROOT%
pause
exit /b 1

:fail
echo.
echo The renewal stopped at a step above. Read the error message and run this file again.
echo The server file, if it was created, is still at: %SERVER%
pause
exit /b 1
