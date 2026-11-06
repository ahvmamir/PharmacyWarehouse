@echo off
rem Show the state of the PharmacyWarehouse service, port 443, the health URL and recent errors. Changes nothing.
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
title PharmacyWarehouse - health check

echo ============================================
echo 1. Windows service
echo ============================================
powershell -NoProfile -Command "try { Get-Service -Name 'PharmacyWarehouse' -ErrorAction Stop | Format-List Name,DisplayName,Status,StartType } catch { Write-Host 'Service PharmacyWarehouse was not found. Run 1-install.bat first.' }"
echo.

echo ============================================
echo 2. Port 443 listener
echo ============================================
powershell -NoProfile -Command "$c = Get-NetTCPConnection -LocalPort 443 -State Listen -ErrorAction SilentlyContinue; if ($c) { foreach ($x in $c) { $p = Get-Process -Id $x.OwningProcess -ErrorAction SilentlyContinue; Write-Host ('Port 443 is listened on by PID ' + $x.OwningProcess + ', process ' + $p.ProcessName) } } else { Write-Host 'Nothing is listening on port 443.' }"
echo.

echo ============================================
echo 3. Health URL
echo ============================================
curl.exe -k -s -S -m 10 https://localhost/api/health
if errorlevel 1 echo Health check FAILED - curl could not get an answer from https://localhost/api/health
echo.
echo.

echo ============================================
echo 4. Last 5 errors in the Application log
echo ============================================
powershell -NoProfile -Command "try { Get-WinEvent -FilterHashtable @{LogName='Application'; ProviderName='PharmacyWarehouse','.NET Runtime','Application Error'} -MaxEvents 5 -ErrorAction Stop | Format-List TimeCreated,ProviderName,Id,LevelDisplayName,Message } catch { Write-Host 'No matching events were found.' }"
echo.
echo Healthy means: service Running, port 443 listened on, and the health answer shows status ok.
echo.
pause
exit /b 0
