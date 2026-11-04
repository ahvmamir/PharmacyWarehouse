<#
.SYNOPSIS
  Remove the PharmacyWarehouse service, firewall rule, backup task and program folder.
  The data folder (database, certificate, keys, backups) is NEVER deleted.
#>
#Requires -RunAsAdministrator
[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'High')]
param(
    [string]$InstallDir = 'C:\PharmacyWarehouse\app'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$ServiceName = 'PharmacyWarehouse'
$InstallDir = [IO.Path]::GetFullPath($InstallDir)

if (-not $PSCmdlet.ShouldProcess("service $ServiceName and $InstallDir", 'Remove')) { return }

if (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) {
    Stop-Service -Name $ServiceName -ErrorAction SilentlyContinue
    sc.exe delete $ServiceName | Out-Null
}
Get-NetFirewallRule -DisplayName 'PharmacyWarehouse HTTPS' -ErrorAction SilentlyContinue | Remove-NetFirewallRule
Unregister-ScheduledTask -TaskName 'PharmacyWarehouse nightly backup' -Confirm:$false -ErrorAction SilentlyContinue

if (Test-Path $InstallDir) { Remove-Item -Recurse -Force $InstallDir }

Write-Host "PharmacyWarehouse removed. The data folder was kept; delete it by hand only after copying pharmacywarehouse.db somewhere safe." -ForegroundColor Green
