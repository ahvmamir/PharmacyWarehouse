<#
.SYNOPSIS
  Remove the Emu service, firewall rule, backup task and program folder.
  The data folder (database, certificate, keys, backups) is NEVER deleted.
#>
#Requires -RunAsAdministrator
[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'High')]
param(
    [string]$InstallDir = 'C:\Emu\app'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$ServiceName = 'Emu'
$InstallDir = [IO.Path]::GetFullPath($InstallDir)

if (-not $PSCmdlet.ShouldProcess("service $ServiceName and $InstallDir", 'Remove')) { return }

if (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) {
    Stop-Service -Name $ServiceName -ErrorAction SilentlyContinue
    sc.exe delete $ServiceName | Out-Null
}
Get-NetFirewallRule -DisplayName 'Emu HTTPS' -ErrorAction SilentlyContinue | Remove-NetFirewallRule
Unregister-ScheduledTask -TaskName 'Emu nightly backup' -Confirm:$false -ErrorAction SilentlyContinue

if (Test-Path $InstallDir) { Remove-Item -Recurse -Force $InstallDir }

Write-Host "Emu removed. The data folder was kept; delete it by hand only after copying emu.db somewhere safe." -ForegroundColor Green
