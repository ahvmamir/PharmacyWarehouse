<#
.SYNOPSIS
  Replace the HTTPS certificate of an existing PharmacyWarehouse installation.

.DESCRIPTION
  Run in an elevated PowerShell. Steps:
    1. keep the current certificate and settings as *.previous
    2. store the new certificate with a fresh random password
    3. add -AddHost names to AllowedHosts (an IP that is not listed there gets HTTP 400)
    4. restart the service and check /api/health, printing the real error if it fails

  The database and the rest of the settings are not touched.
  Messages are English on purpose: Windows PowerShell 5.1 reads a .ps1 file
  without BOM as ANSI, so Persian text would be garbled.

.EXAMPLE
  .\apply-certificate.ps1 -PfxPath E:\PharmacyCA\pharmacywarehouse-server.pfx -AddHost 192.168.1.20,pharmacy-pc
#>
#Requires -RunAsAdministrator
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PfxPath,
    [SecureString]$PfxPassword,
    # Names and IPs users type in the browser; should match the certificate.
    [string[]]$AddHost = @(),
    [string]$InstallDir = 'C:\PharmacyWarehouse\app'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$ServiceName = 'PharmacyWarehouse'
$settingsPath = Join-Path $InstallDir 'appsettings.Production.json'

function Step([string]$text) { Write-Host "==> $text" -ForegroundColor Cyan }

function New-RandomSecret {
    $bytes = New-Object byte[] 32
    $generator = [Security.Cryptography.RandomNumberGenerator]::Create()
    try { $generator.GetBytes($bytes) } finally { $generator.Dispose() }
    return [Convert]::ToBase64String($bytes)
}

if (-not (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue)) { throw "Service '$ServiceName' not found. Use install.ps1 first." }
if (-not (Test-Path $settingsPath)) { throw "$settingsPath not found." }
if (-not (Test-Path $PfxPath)) { throw "$PfxPath not found." }
if (-not $PfxPassword) { $PfxPassword = Read-Host -AsSecureString "Password of $PfxPath" }

# UTF8 explicitly: the file has no BOM and may hold a Persian organization name.
$settings = Get-Content -Raw -Encoding UTF8 -Path $settingsPath | ConvertFrom-Json
$endpoint = $settings.Kestrel.Endpoints.Https
$pfxTarget = $endpoint.Certificate.Path
$port = ([Uri]($endpoint.Url -replace '\*', 'localhost')).Port
$certDir = Split-Path -Parent $pfxTarget
$cerTarget = Join-Path $certDir 'pharmacywarehouse.cer'

$secret = New-RandomSecret
$secure = ConvertTo-SecureString $secret -AsPlainText -Force

# Validate and re-export before touching anything, so a wrong password changes nothing.
Step 'Reading the new certificate'
$certificate = Import-PfxCertificate -FilePath $PfxPath -Password $PfxPassword -CertStoreLocation Cert:\LocalMachine\My -Exportable
$pending = "$pfxTarget.new"
try {
    if ($certificate.NotAfter -lt (Get-Date)) { throw "This certificate expired on $($certificate.NotAfter)." }
    Export-PfxCertificate -Cert $certificate -FilePath $pending -Password $secure -ChainOption EndEntityCertOnly -Force | Out-Null
    $cerBytes = $certificate.Export([Security.Cryptography.X509Certificates.X509ContentType]::Cert)
}
finally {
    Remove-Item -Path "Cert:\LocalMachine\My\$($certificate.Thumbprint)" -DeleteKey
}

Step "Stopping service '$ServiceName'"
Stop-Service -Name $ServiceName

Step 'Replacing the certificate and settings (old copies: *.previous)'
Copy-Item -Path $settingsPath -Destination "$settingsPath.previous" -Force
if (Test-Path $pfxTarget) { Copy-Item -Path $pfxTarget -Destination "$pfxTarget.previous" -Force }
Move-Item -Path $pending -Destination $pfxTarget -Force
[IO.File]::WriteAllBytes($cerTarget, $cerBytes)

$hosts = @($settings.AllowedHosts -split ';') + @($AddHost) | Where-Object { $_ } | Select-Object -Unique
$settings.AllowedHosts = $hosts -join ';'
$endpoint.Certificate.Password = $secret
[IO.File]::WriteAllText($settingsPath, ($settings | ConvertTo-Json -Depth 6), (New-Object Text.UTF8Encoding($false)))

Step "Starting service '$ServiceName'"
Start-Service -Name $ServiceName

[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
[Net.ServicePointManager]::ServerCertificateValidationCallback = { $true }
$healthy = $false
$lastError = $null
for ($i = 0; $i -lt 30 -and -not $healthy; $i++) {
    Start-Sleep -Seconds 1
    try {
        $healthy = (Invoke-WebRequest -UseBasicParsing -Uri "https://localhost:$port/api/health" -TimeoutSec 5).StatusCode -eq 200
    }
    catch { $lastError = $_.Exception.Message }
}
[Net.ServicePointManager]::ServerCertificateValidationCallback = $null

Write-Host ''
if ($healthy) {
    Write-Host "Certificate applied and /api/health answered. AllowedHosts: $($settings.AllowedHosts)" -ForegroundColor Green
    Write-Host "Delete $PfxPath if it is not needed any more; it holds the server private key."
}
else {
    Write-Warning "/api/health did not answer within 30 seconds. Last error: $lastError"
    Write-Host 'To go back: Stop-Service PharmacyWarehouse, copy the two *.previous files over the originals, Start-Service PharmacyWarehouse.'
    Write-Host "Previous files: $settingsPath.previous and $pfxTarget.previous"
}
