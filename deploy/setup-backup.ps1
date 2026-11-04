<#
.SYNOPSIS
  Run on the Emu VM: create the nightly backup task, run it once and check the result.

.DESCRIPTION
  Workgroup (no domain): the task runs as a local user that has the SAME name and password
  as the user created by setup-backup-target.ps1 on the backup computer; Windows then signs
  in to the share with it. No password is stored in any script (Task Scheduler keeps it).

  Steps:
    1. create (or update) the local user, password never expires, member of Administrators
       (the database folder is readable only by Administrators, SYSTEM and the service)
    2. register "Emu nightly backup" (daily) as that user; output goes to DataDir\backup-last.log
    3. run it now, wait, and report success (new backup time) or the log on failure

  Re-running is safe: the task is replaced, the user's password is updated.

.EXAMPLE
  .\setup-backup.ps1 -BackupDir \\BACKUP-PC\EmuBackups
#>
#Requires -RunAsAdministrator
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$BackupDir,
    [string]$User = 'emubackup',
    [string]$Time = '02:30',
    [string]$InstallDir = 'C:\Emu\app'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$TaskName = 'Emu nightly backup'
$InstallDir = [IO.Path]::GetFullPath($InstallDir)
$Exe = Join-Path $InstallDir 'Emu.Api.exe'
$settingsPath = Join-Path $InstallDir 'appsettings.Production.json'

function Step([string]$text) { Write-Host "==> $text" -ForegroundColor Cyan }

function Plain([SecureString]$secure) {
    [Runtime.InteropServices.Marshal]::PtrToStringBSTR([Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure))
}

# ---------- checks ----------
if (-not (Test-Path $Exe)) { throw "$Exe not found. Install Emu first (install.ps1) or pass -InstallDir." }
if (-not (Test-Path $settingsPath)) { throw "$settingsPath not found." }
if (-not $BackupDir.StartsWith('\\')) {
    Write-Warning "$BackupDir is not a network path. A backup on this VM is lost together with the VM; prefer a share on another computer."
}

$settings = Get-Content -Raw -Path $settingsPath | ConvertFrom-Json
$dbPath = ($settings.ConnectionStrings.Default -replace '^\s*Data Source\s*=\s*', '').Trim()
$DataDir = Split-Path -Parent $dbPath
$marker = "$dbPath.last-backup"
$log = Join-Path $DataDir 'backup-last.log'

# ---------- user ----------
while ($true) {
    $password = Read-Host -AsSecureString "Password for '$User' (the SAME as on the backup computer)"
    $repeat = Read-Host -AsSecureString 'Repeat the password'
    $plain = Plain $password
    if ($plain.Length -lt 8) { Write-Warning 'At least 8 characters.'; continue }
    if ($plain -ne (Plain $repeat)) { Write-Warning 'The two passwords differ. Try again.'; continue }
    break
}

Step "Local user '$User'"
if (Get-LocalUser -Name $User -ErrorAction SilentlyContinue) {
    Set-LocalUser -Name $User -Password $password -PasswordNeverExpires $true
    Write-Host 'User existed; password updated.'
}
else {
    New-LocalUser -Name $User -Password $password -PasswordNeverExpires -AccountNeverExpires `
        -Description 'Emu nightly backup' | Out-Null
}
# S-1-5-32-544 = Administrators (name differs on non-English Windows). Add directly instead of listing
# members first: Get-LocalGroupMember fails on groups that contain a deleted account.
try { Add-LocalGroupMember -SID S-1-5-32-544 -Member $User -ErrorAction Stop }
catch [Microsoft.PowerShell.Commands.MemberExistsException] { }

# ---------- task ----------
Step "Scheduling '$TaskName' daily at $Time to $BackupDir"
# cmd /c keeps the program's output in a log, because a scheduled task has no console to read.
$command = "/c `"`"$Exe`" backup `"$BackupDir`" > `"$log`" 2>&1`""
$action = New-ScheduledTaskAction -Execute 'cmd.exe' -Argument $command -WorkingDirectory $InstallDir
$trigger = New-ScheduledTaskTrigger -Daily -At $Time
# Missed runs (VM off at night) run at the next start; a run never takes more than an hour.
$taskSettings = New-ScheduledTaskSettingsSet -StartWhenAvailable -ExecutionTimeLimit (New-TimeSpan -Hours 1)
Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger -Settings $taskSettings -RunLevel Highest `
    -User "$env:COMPUTERNAME\$User" -Password $plain -Force | Out-Null
$plain = $null

# ---------- run once ----------
Step 'Running the backup once now'
$before = if (Test-Path $marker) { (Get-Item $marker).LastWriteTimeUtc } else { [DateTime]::MinValue }
Start-ScheduledTask -TaskName $TaskName

$deadline = (Get-Date).AddSeconds(120)
do {
    Start-Sleep -Seconds 3
    $state = (Get-ScheduledTask -TaskName $TaskName).State
} while ($state -eq 'Running' -and (Get-Date) -lt $deadline)

$result = (Get-ScheduledTaskInfo -TaskName $TaskName).LastTaskResult
$after = if (Test-Path $marker) { (Get-Item $marker).LastWriteTimeUtc } else { [DateTime]::MinValue }

Write-Host ''
if ($result -eq 0 -and $after -gt $before) {
    Write-Host "Backup works. A new emu-*.db is in $BackupDir; the task runs daily at $Time." -ForegroundColor Green
    Write-Host 'The "no backup" alert in Emu disappears on the next page change.'
    Write-Host 'Next: test a restore once (docs\deploy.md, restore section).'
}
else {
    Write-Warning "The backup did not succeed (task result $result)."
    if (Test-Path $log) {
        Write-Host '--- backup-last.log ---'
        Get-Content $log | Select-Object -Last 15 | ForEach-Object { Write-Host $_ }
        Write-Host '-----------------------'
    }
    Write-Host 'Most common causes:'
    Write-Host "  - user name or password of '$User' differs from the one on the backup computer"
    Write-Host '  - the share name is wrong, or the backup computer is off'
    Write-Host '  - File and Printer Sharing is blocked by the firewall on the backup computer'
    Write-Host 'Fix the cause and run this script again; it replaces the task.'
    exit 1
}
