<#
.SYNOPSIS
  First-time install of PharmacyWarehouse as a Windows Service (no IIS, no .NET install needed).

.DESCRIPTION
  Run from the extracted package folder (the one that contains .\app) in an
  elevated PowerShell. Steps:
    1. copy .\app to InstallDir
    2. create or import the HTTPS certificate into DataDir\cert
    3. write InstallDir\appsettings.Production.json (never overwritten by update.ps1)
    4. register the "PharmacyWarehouse" service under the virtual account "NT SERVICE\PharmacyWarehouse"
    5. lock down DataDir permissions, create the database (migrate)
    6. optionally create the first admin, firewall rule and nightly backup task
    7. start the service and check /api/health

  Messages are English on purpose: Windows PowerShell 5.1 reads a .ps1 file
  without BOM as ANSI, so Persian text would be garbled.

.EXAMPLE
  .\install.ps1 -HostName pharmacy-pc,pharmacy-pc.local -DataDir D:\PharmacyWarehouseData -AdminUser admin1 -BackupDir \\backup-server\PharmacyWarehouseBackups

.EXAMPLE
  Workgroup (no domain, no DNS): open the app by the server PC fixed IP.
  .\install.ps1 -IpAddress 10.0.0.5 -DataDir D:\PharmacyWarehouseData -AdminUser admin1 -AllowFrom 10.0.0.0/24
#>
#Requires -RunAsAdministrator
[CmdletBinding()]
param(
    # Names users type in the browser (short name and FQDN). Go into the certificate and AllowedHosts.
    [string[]]$HostName = @($env:COMPUTERNAME),
    # Fixed IP(s) users may type instead of a name (workgroup without DNS). Go into the certificate and AllowedHosts.
    [string[]]$IpAddress = @(),
    [string]$InstallDir = 'C:\PharmacyWarehouse\app',
    # Database, certificate and keys. Keep it outside InstallDir; ideally on a data disk.
    [string]$DataDir = 'C:\PharmacyWarehouseData',
    [int]$Port = 443,
    # Optional PFX from the organization CA. Without it a self-signed certificate is created.
    [string]$PfxPath,
    [SecureString]$PfxPassword,
    # Firewall RemoteAddress: LocalSubnet, an address range like 10.1.2.0/24, or Any.
    [string]$AllowFrom = 'LocalSubnet',
    # Optional first admin; the password is asked interactively (min 8 chars).
    [string]$AdminUser,
    # Optional folder for the nightly backup task. Should be OUTSIDE this PC.
    [string]$BackupDir,
    # Needed when BackupDir is a network share and there is no domain: a Windows account that can
    # write to the share and is a local Administrator here (to read the database). Asked if omitted.
    [PSCredential]$BackupCredential,
    [string]$BackupTime = '02:30',
    # Pharmacy name shown in the header, login page, printed reports and labels. Optional; can be
    # set later in appsettings.Production.json (Branding:OrganizationName) and a service restart.
    [string]$OrganizationName
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$ServiceName = 'PharmacyWarehouse'
$ServiceAccount = "NT SERVICE\$ServiceName"
$FirewallRule = 'PharmacyWarehouse HTTPS'
$BackupTask = 'PharmacyWarehouse nightly backup'
$Exe = Join-Path $InstallDir 'PharmacyWarehouse.Api.exe'
$Source = Join-Path $PSScriptRoot 'app'

function Step([string]$text) { Write-Host "==> $text" -ForegroundColor Cyan }

function Invoke-PharmacyWarehouse([string[]]$arguments) {
    # The app reads appsettings from the current folder when not running as a service.
    Push-Location $InstallDir
    try {
        & $Exe @arguments
        if ($LASTEXITCODE -ne 0) { throw "PharmacyWarehouse.Api.exe $($arguments -join ' ') failed with exit code $LASTEXITCODE." }
    }
    finally { Pop-Location }
}

function Write-Utf8([string]$path, [string]$text) {
    [IO.File]::WriteAllText($path, $text, (New-Object Text.UTF8Encoding $false))
}

function New-RandomSecret {
    $bytes = New-Object byte[] 24
    [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
    [Convert]::ToBase64String($bytes)
}

# ---------- checks ----------
# Windows Server 2016 (build 14393) or newer: New-SelfSignedCertificate -NotAfter/-KeyAlgorithm and
# current .NET need it. Server Core works too (no GUI is used).
$osBuild = [Environment]::OSVersion.Version.Build
if ($osBuild -lt 14393) {
    throw "Windows build $osBuild is too old. Use Windows Server 2016 or newer."
}
if (Get-Service -Name W3SVC -ErrorAction SilentlyContinue) {
    Write-Warning "IIS (W3SVC) is installed. If it already uses port $Port, the check below stops; then pass another -Port."
}
if (-not (Test-Path (Join-Path $Source 'PharmacyWarehouse.Api.exe'))) {
    throw "Package folder .\app\PharmacyWarehouse.Api.exe not found next to this script. Build it with publish-package.bat."
}
foreach ($ip in $IpAddress) {
    $parsed = $null
    if (-not [Net.IPAddress]::TryParse($ip, [ref]$parsed)) { throw "-IpAddress '$ip' is not a valid IP address." }
}
$isDomainMember = (Get-CimInstance Win32_ComputerSystem).PartOfDomain
if ($BackupDir -and $BackupDir.StartsWith('\\') -and -not $isDomainMember -and -not $BackupCredential) {
    # Without a domain, SYSTEM reaches a share of another computer as an anonymous user and is refused.
    $BackupCredential = Get-Credential -Message "Windows account for the nightly backup to $BackupDir (must be able to write there and be a local Administrator here)"
    if (-not $BackupCredential) { throw "A backup account is required for a network BackupDir without a domain." }
}
if (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) {
    throw "Service '$ServiceName' already exists. Use update.ps1 to install a new version."
}
$InstallDir = [IO.Path]::GetFullPath($InstallDir)
$DataDir = [IO.Path]::GetFullPath($DataDir)
$Exe = Join-Path $InstallDir 'PharmacyWarehouse.Api.exe'
if ($DataDir.TrimEnd('\').StartsWith($InstallDir.TrimEnd('\'), [StringComparison]::OrdinalIgnoreCase)) {
    throw "DataDir must not be inside InstallDir; updates replace InstallDir."
}
if (Test-Path (Join-Path $DataDir 'pharmacywarehouse.db')) {
    Write-Warning "An existing database was found in $DataDir. It will be kept and migrated."
}
$listener = Get-NetTCPConnection -State Listen -LocalPort $Port -ErrorAction SilentlyContinue
if ($listener) {
    throw "Port $Port is already in use (process id $($listener[0].OwningProcess)). Choose another -Port or stop that program (IIS?)."
}

# ---------- files ----------
Step "Copying application to $InstallDir"
New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
robocopy $Source $InstallDir /MIR /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -ge 8) { throw "robocopy failed with exit code $LASTEXITCODE." }

$certDir = Join-Path $DataDir 'cert'
$keysDir = Join-Path $DataDir 'keys'
New-Item -ItemType Directory -Force -Path $DataDir, $certDir, $keysDir | Out-Null

# ---------- certificate ----------
$pfxTarget = Join-Path $certDir 'pharmacywarehouse.pfx'
$cerTarget = Join-Path $certDir 'pharmacywarehouse.cer'
$pfxSecret = New-RandomSecret
$pfxSecure = ConvertTo-SecureString $pfxSecret -AsPlainText -Force

if ($PfxPath) {
    Step "Importing certificate $PfxPath"
    if (-not $PfxPassword) { $PfxPassword = Read-Host -AsSecureString "Password of $PfxPath" }
    # Re-export with a fresh random password so the CA password is not stored on disk.
    $cert = Import-PfxCertificate -FilePath $PfxPath -Password $PfxPassword -CertStoreLocation Cert:\LocalMachine\My -Exportable
}
else {
    $names = @($HostName) + 'localhost' | Select-Object -Unique
    Step "Creating self-signed certificate for $((@($names) + @($IpAddress)) -join ', ') (valid 5 years)"
    # Browsers accept an IP only as an IPAddress entry of the Subject Alternative Name, not as a DNS entry,
    # so the SAN is written explicitly (OID 2.5.29.17).
    $san = (@($names | ForEach-Object { "DNS=$_" }) + @($IpAddress | ForEach-Object { "IPAddress=$_" })) -join '&'
    $cert = New-SelfSignedCertificate -Subject "CN=$(@($names)[0])" -TextExtension @("2.5.29.17={text}$san") `
        -CertStoreLocation Cert:\LocalMachine\My `
        -NotAfter (Get-Date).AddYears(5) -KeyAlgorithm RSA -KeyLength 2048 -KeyExportPolicy Exportable `
        -FriendlyName 'PharmacyWarehouse HTTPS'
}
Export-PfxCertificate -Cert $cert -FilePath $pfxTarget -Password $pfxSecure | Out-Null
Export-Certificate -Cert $cert -FilePath $cerTarget | Out-Null
# The app reads the PFX file; the store copy is not needed.
Remove-Item -Path "Cert:\LocalMachine\My\$($cert.Thumbprint)" -DeleteKey

# ---------- settings ----------
Step "Writing appsettings.Production.json"
$settings = [ordered]@{
    ConnectionStrings = [ordered]@{ Default = "Data Source=$(Join-Path $DataDir 'pharmacywarehouse.db')" }
    AllowedHosts      = (@($HostName) + @($IpAddress) + 'localhost' | Select-Object -Unique) -join ';'
    DataProtection    = [ordered]@{ KeysPath = $keysDir }
    Branding          = [ordered]@{ OrganizationName = $OrganizationName }
    Kestrel           = [ordered]@{
        Endpoints = [ordered]@{
            Https = [ordered]@{
                Url         = "https://*:$Port"
                Certificate = [ordered]@{ Path = $pfxTarget; Password = $pfxSecret }
            }
        }
    }
}
$settingsPath = Join-Path $InstallDir 'appsettings.Production.json'
Write-Utf8 $settingsPath ($settings | ConvertTo-Json -Depth 6)

# ---------- service ----------
Step "Registering Windows service '$ServiceName' as $ServiceAccount"
New-Service -Name $ServiceName -BinaryPathName "`"$Exe`"" -DisplayName 'PharmacyWarehouse - Pharmacy Warehouse' `
    -Description 'Pharmacy warehouse inventory (PharmacyWarehouse).' -StartupType Automatic | Out-Null
sc.exe config $ServiceName obj= $ServiceAccount | Out-Null
if ($LASTEXITCODE -ne 0) { throw "sc.exe config failed ($LASTEXITCODE)." }
# Restart automatically after a crash (1 minute delay, counter resets daily).
sc.exe failure $ServiceName reset= 86400 actions= restart/60000/restart/60000/restart/60000 | Out-Null

# ---------- permissions ----------
# SIDs instead of names: group names differ on non-English Windows.
# S-1-5-32-544 = Administrators, S-1-5-18 = SYSTEM.
Step "Restricting permissions on $DataDir and the settings file"
# Rights are set on the folder only; existing files (certificate, keys) are then reset to inherit them.
# Granting with /T instead left the service without read access to pharmacywarehouse.pfx on a real install.
icacls $DataDir /inheritance:r /grant:r '*S-1-5-32-544:(OI)(CI)F' '*S-1-5-18:(OI)(CI)F' "${ServiceAccount}:(OI)(CI)M" /Q | Out-Null
if ($LASTEXITCODE -ne 0) { throw "icacls on $DataDir failed ($LASTEXITCODE)." }
icacls "$DataDir\*" /reset /T /C /Q | Out-Null
if ($LASTEXITCODE -ne 0) { throw "icacls reset inside $DataDir failed ($LASTEXITCODE)." }
# A folder created under C:\ inherits "Authenticated Users: Modify" from the drive root, so any user
# who can log on could replace PharmacyWarehouse.Api.exe. Only Administrators and SYSTEM may change the program;
# the service may read and execute it.
icacls $InstallDir /inheritance:r /grant:r '*S-1-5-32-544:(OI)(CI)F' '*S-1-5-18:(OI)(CI)F' "${ServiceAccount}:(OI)(CI)RX" /Q | Out-Null
if ($LASTEXITCODE -ne 0) { throw "icacls on $InstallDir failed ($LASTEXITCODE)." }
icacls "$InstallDir\*" /reset /T /C /Q | Out-Null
if ($LASTEXITCODE -ne 0) { throw "icacls reset inside $InstallDir failed ($LASTEXITCODE)." }
# Holds the certificate password.
icacls $settingsPath /inheritance:r /grant:r '*S-1-5-32-544:F' '*S-1-5-18:F' "${ServiceAccount}:R" /Q | Out-Null
if ($LASTEXITCODE -ne 0) { throw "icacls on settings failed ($LASTEXITCODE)." }

# ---------- database ----------
Step "Creating or migrating the database"
Invoke-PharmacyWarehouse @('migrate')

if ($AdminUser) {
    Step "Creating admin '$AdminUser' (password: at least 8 characters)"
    Invoke-PharmacyWarehouse @('create-admin', $AdminUser)
}

# ---------- firewall ----------
Step "Firewall: allow TCP $Port from $AllowFrom"
Get-NetFirewallRule -DisplayName $FirewallRule -ErrorAction SilentlyContinue | Remove-NetFirewallRule
New-NetFirewallRule -DisplayName $FirewallRule -Direction Inbound -Action Allow -Protocol TCP `
    -LocalPort $Port -RemoteAddress $AllowFrom -Program $Exe -Profile Any | Out-Null
# All profiles: a server outside a domain is usually on a "Public" network. The address filter
# (-AllowFrom) is what limits access, so it should name the pharmacy subnet rather than Any.
if ($AllowFrom -eq 'Any') {
    Write-Warning "Firewall allows port $Port from ANY address. Prefer -AllowFrom with the pharmacy subnet."
}

# ---------- backup task ----------
if ($BackupDir) {
    Step "Scheduling nightly backup at $BackupTime to $BackupDir"
    $action = New-ScheduledTaskAction -Execute $Exe -Argument "backup `"$BackupDir`"" -WorkingDirectory $InstallDir
    $trigger = New-ScheduledTaskTrigger -Daily -At $BackupTime
    if ($BackupCredential) {
        # Task Scheduler stores the password (encrypted). If that password changes, the task fails.
        Register-ScheduledTask -TaskName $BackupTask -Action $action -Trigger $trigger -RunLevel Highest `
            -User $BackupCredential.UserName -Password $BackupCredential.GetNetworkCredential().Password -Force | Out-Null
    }
    else {
        $principal = New-ScheduledTaskPrincipal -UserId 'SYSTEM' -LogonType ServiceAccount -RunLevel Highest
        Register-ScheduledTask -TaskName $BackupTask -Action $action -Trigger $trigger -Principal $principal -Force | Out-Null
    }
    Write-Host "Test it now: Start-ScheduledTask -TaskName '$BackupTask'  then look for a new pharmacywarehouse-*.db in $BackupDir"
}

# ---------- start ----------
Step "Starting service"
Start-Service -Name $ServiceName

[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
[Net.ServicePointManager]::ServerCertificateValidationCallback = { $true }
$healthy = $false
for ($i = 0; $i -lt 30 -and -not $healthy; $i++) {
    Start-Sleep -Seconds 1
    try {
        $response = Invoke-WebRequest -UseBasicParsing -Uri "https://localhost:$Port/api/health" -TimeoutSec 5
        $healthy = $response.StatusCode -eq 200
    }
    catch { }
}
[Net.ServicePointManager]::ServerCertificateValidationCallback = $null

$mainName = if (@($IpAddress).Count -gt 0) { @($IpAddress)[0] } else { @($HostName)[0] }
$url = if ($Port -eq 443) { "https://$mainName/" } else { "https://${mainName}:$Port/" }
Write-Host ''
if ($healthy) {
    Write-Host "PharmacyWarehouse is running: $url" -ForegroundColor Green
}
else {
    Write-Warning "Service started but /api/health did not answer within 30 seconds. Check Event Viewer > Windows Logs > Application (source: PharmacyWarehouse)."
}
if (-not $PfxPath) {
    Write-Host "Self-signed certificate: distribute $cerTarget to client PCs as a Trusted Root (Group Policy is best), or browsers will warn."
}
if (-not $AdminUser) {
    Write-Host "Create the first admin:  cd `"$InstallDir`" ; .\PharmacyWarehouse.Api.exe create-admin <username>"
}
if (-not $BackupDir) {
    Write-Warning "No nightly backup task was created. See docs\deploy.md (backup section) to add one."
}
