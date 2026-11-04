<#
.SYNOPSIS
  Install a new Emu version over an existing install.ps1 installation.

.DESCRIPTION
  Run from the extracted NEW package folder (the one that contains .\app) in an
  elevated PowerShell. Steps:
    1. backup the database (VACUUM INTO, safe while running) to DataDir\pre-update
    2. stop the service and keep the old program in InstallDir.previous
    3. copy the new program, keeping appsettings.Production.json
    4. migrate the database, start the service, check /api/health

  Rollback (migrations are not reversible): stop the service, copy
  InstallDir.previous back over InstallDir, restore the pre-update backup as
  emu.db (see docs\deploy.md), start the service.
#>
#Requires -RunAsAdministrator
[CmdletBinding()]
param(
    [string]$InstallDir = 'C:\Emu\app',
    [int]$Port = 0
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$ServiceName = 'Emu'
$Source = Join-Path $PSScriptRoot 'app'
$InstallDir = [IO.Path]::GetFullPath($InstallDir)
$Exe = Join-Path $InstallDir 'Emu.Api.exe'
$Previous = "$($InstallDir.TrimEnd('\')).previous"
$settingsPath = Join-Path $InstallDir 'appsettings.Production.json'

function Step([string]$text) { Write-Host "==> $text" -ForegroundColor Cyan }

function Invoke-Emu([string[]]$arguments) {
    Push-Location $InstallDir
    try {
        & $Exe @arguments
        if ($LASTEXITCODE -ne 0) { throw "Emu.Api.exe $($arguments -join ' ') failed with exit code $LASTEXITCODE." }
    }
    finally { Pop-Location }
}

if (-not (Test-Path (Join-Path $Source 'Emu.Api.exe'))) { throw "New package .\app\Emu.Api.exe not found next to this script." }
if (-not (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue)) { throw "Service '$ServiceName' not found. Use install.ps1 first." }
if (-not (Test-Path $settingsPath)) { throw "$settingsPath not found." }

# Data folder and port come from the existing settings, so they cannot drift.
$settings = Get-Content -Raw -Path $settingsPath | ConvertFrom-Json
$dbPath = ($settings.ConnectionStrings.Default -replace '^\s*Data Source\s*=\s*', '').Trim()
$DataDir = Split-Path -Parent $dbPath
if ($Port -eq 0) {
    $Port = [int](($settings.Kestrel.Endpoints.Https.Url -split ':')[-1])
}

Step "Backing up the database to $DataDir\pre-update"
Invoke-Emu @('backup', (Join-Path $DataDir 'pre-update'))

Step "Stopping service"
Stop-Service -Name $ServiceName
(Get-Service -Name $ServiceName).WaitForStatus('Stopped', [TimeSpan]::FromSeconds(30))

Step "Keeping the current program in $Previous"
robocopy $InstallDir $Previous /MIR /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -ge 8) { throw "robocopy (keep previous) failed with exit code $LASTEXITCODE." }

try {
    Step "Copying new program"
    # /XF keeps appsettings.Production.json: robocopy neither overwrites nor purges excluded files.
    robocopy $Source $InstallDir /MIR /XF appsettings.Production.json /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "robocopy (new version) failed with exit code $LASTEXITCODE." }

    Step "Migrating database"
    Invoke-Emu @('migrate')
}
catch {
    Write-Warning "Update failed: $($_.Exception.Message)"
    Write-Warning "The service is stopped. To roll back: copy $Previous over $InstallDir, restore the newest file from $DataDir\pre-update as emu.db (docs\deploy.md), then Start-Service $ServiceName."
    throw
}

Step "Starting service"
Start-Service -Name $ServiceName

[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
[Net.ServicePointManager]::ServerCertificateValidationCallback = { $true }
$healthy = $false
for ($i = 0; $i -lt 30 -and -not $healthy; $i++) {
    Start-Sleep -Seconds 1
    try { $healthy = (Invoke-WebRequest -UseBasicParsing -Uri "https://localhost:$Port/api/health" -TimeoutSec 5).StatusCode -eq 200 }
    catch { }
}
[Net.ServicePointManager]::ServerCertificateValidationCallback = $null

if ($healthy) {
    Write-Host "Update finished. Users should reload the page with Ctrl+F5." -ForegroundColor Green
}
else {
    Write-Warning "Service started but /api/health did not answer. Check Event Viewer > Application (source: Emu)."
}
