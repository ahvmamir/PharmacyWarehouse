<#
.SYNOPSIS
  Run on the BACKUP computer (not the pharmacy server PC): create the backup user, folder and share.

.DESCRIPTION
  Workgroup (no domain): the pharmacy server PC reaches this share with a local account that has the
  SAME name and password on both computers. Run setup-backup.ps1 on the server PC afterwards with
  the same user name and password.

  Steps:
    1. create (or update the password of) the local user, password never expires
    2. create the folder and give only that user (plus Administrators/SYSTEM) write access
    3. share it with change access for that user only

.EXAMPLE
  .\setup-backup-target.ps1 -Folder D:\PharmacyWarehouseBackups
#>
#Requires -RunAsAdministrator
[CmdletBinding()]
param(
    [string]$Folder = 'C:\PharmacyWarehouseBackups',
    [string]$ShareName = 'PharmacyWarehouseBackups',
    [string]$User = 'pwbackup'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Step([string]$text) { Write-Host "==> $text" -ForegroundColor Cyan }

function Read-NewPassword([string]$user) {
    while ($true) {
        $first = Read-Host -AsSecureString "Password for '$user' (use the same one on the pharmacy server PC)"
        $second = Read-Host -AsSecureString "Repeat the password"
        $a = [Runtime.InteropServices.Marshal]::PtrToStringBSTR([Runtime.InteropServices.Marshal]::SecureStringToBSTR($first))
        $b = [Runtime.InteropServices.Marshal]::PtrToStringBSTR([Runtime.InteropServices.Marshal]::SecureStringToBSTR($second))
        if ($a.Length -lt 8) { Write-Warning 'At least 8 characters.'; continue }
        if ($a -ne $b) { Write-Warning 'The two passwords differ. Try again.'; continue }
        return $first
    }
}

$password = Read-NewPassword $User

Step "Local user '$User'"
if (Get-LocalUser -Name $User -ErrorAction SilentlyContinue) {
    Set-LocalUser -Name $User -Password $password -PasswordNeverExpires $true
    Write-Host "User existed; password updated."
}
else {
    New-LocalUser -Name $User -Password $password -PasswordNeverExpires -AccountNeverExpires `
        -Description 'PharmacyWarehouse nightly backup (writes to the backup share only)' | Out-Null
}

Step "Folder $Folder"
New-Item -ItemType Directory -Force -Path $Folder | Out-Null
# Only Administrators, SYSTEM and the backup user. SIDs: names differ on non-English Windows.
icacls $Folder /inheritance:r /grant:r '*S-1-5-32-544:(OI)(CI)F' '*S-1-5-18:(OI)(CI)F' "${User}:(OI)(CI)M" /Q | Out-Null
if ($LASTEXITCODE -ne 0) { throw "icacls failed ($LASTEXITCODE)." }

Step "Share \\$env:COMPUTERNAME\$ShareName"
if (Get-SmbShare -Name $ShareName -ErrorAction SilentlyContinue) {
    Write-Host "Share exists; access for '$User' is (re)granted."
}
else {
    New-SmbShare -Name $ShareName -Path $Folder -ChangeAccess "$env:COMPUTERNAME\$User" | Out-Null
}
Grant-SmbShareAccess -Name $ShareName -AccountName "$env:COMPUTERNAME\$User" -AccessRight Change -Force | Out-Null

# File sharing must be allowed through this computer's firewall.
$sharing = Get-NetFirewallRule -Group '@FirewallAPI.dll,-28502' -ErrorAction SilentlyContinue | Where-Object { $_.Direction -eq 'Inbound' }
if ($sharing -and -not ($sharing | Where-Object { $_.Enabled -eq 'True' })) {
    Write-Warning 'File and Printer Sharing is blocked by the firewall here. Enable it for the network the pharmacy server PC is on.'
}

$address = @(Get-NetIPAddress -AddressFamily IPv4 -ErrorAction SilentlyContinue | Where-Object { $_.IPAddress -notlike '127.*' -and $_.IPAddress -notlike '169.254.*' } | Select-Object -ExpandProperty IPAddress)
Write-Host ''
Write-Host 'Done. On the pharmacy server PC run (same user and password):' -ForegroundColor Green
Write-Host "  .\setup-backup.ps1 -BackupDir \\$env:COMPUTERNAME\$ShareName"
if ($address.Count -gt 0) {
    Write-Host "  If the name does not resolve from the server PC, use the IP instead: \\$($address[0])\$ShareName"
}
