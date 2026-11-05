<#
.SYNOPSIS
  Create a local certificate authority (CA) and an HTTPS certificate for the PharmacyWarehouse server.

.DESCRIPTION
  Devices trust the CA root once; later server certificates need no change on the devices.
  Administrator rights are not needed. Temporary copies in the current user store are removed at the end.

  Files written to OutDir:
    pharmacywarehouse-root.pfx    CA private key. Keep it OFF the server, for example on a USB stick in a safe place.
    pharmacywarehouse-root.cer    Public CA certificate for Windows and iPhone.
    pharmacywarehouse-root.crt    Same bytes as the .cer, for Android file pickers.
    pharmacywarehouse-server.pfx  For the server. Use it with install.ps1 -PfxPath or apply-certificate.ps1, then delete it.

  Messages are English on purpose: Windows PowerShell 5.1 reads a .ps1 file
  without BOM as ANSI, so Persian text would be garbled.

.EXAMPLE
  First time:
  .\setup-local-ca.ps1 -OutDir E:\PharmacyCA -HostName pharmacy-pc -IpAddress 192.168.1.20

.EXAMPLE
  Renew the server certificate with the same CA; devices need no change:
  .\setup-local-ca.ps1 -OutDir E:\PharmacyCA -HostName pharmacy-pc -IpAddress 192.168.1.20 -RootPfx E:\PharmacyCA\pharmacywarehouse-root.pfx
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$OutDir,
    # Names users type in the browser. localhost is always added.
    [string[]]$HostName = @($env:COMPUTERNAME),
    # Fixed IP(s) users type in the browser. Reserve the IP on the router, or the certificate stops matching when it changes.
    [string[]]$IpAddress = @(),
    # Existing CA, for renewing the server certificate.
    [string]$RootPfx,
    [SecureString]$RootPassword,
    [SecureString]$ServerPassword,
    [int]$RootYears = 10,
    # Apple rejects TLS server certificates valid for more than 825 days.
    [ValidateRange(1, 825)][int]$ServerDays = 800
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Step([string]$text) { Write-Host "==> $text" -ForegroundColor Cyan }

function Read-NewPassword([string]$label) {
    $first = Read-Host -AsSecureString "$label (at least 8 characters)"
    $second = Read-Host -AsSecureString "$label, again"
    $a = (New-Object PSCredential('x', $first)).GetNetworkCredential().Password
    $b = (New-Object PSCredential('x', $second)).GetNetworkCredential().Password
    if ($a -cne $b) { throw 'The two passwords differ.' }
    return $first
}

function Assert-Password([SecureString]$password, [string]$label) {
    if ((New-Object PSCredential('x', $password)).GetNetworkCredential().Password.Length -lt 8) {
        throw "$label must be at least 8 characters."
    }
}

function Remove-TemporaryCertificate($certificate) {
    if (-not $certificate) { return }
    $thumbprint = $certificate.Thumbprint
    if (Test-Path "Cert:\CurrentUser\My\$thumbprint") { Remove-Item "Cert:\CurrentUser\My\$thumbprint" -DeleteKey }
    # Windows may also place a copy of a self-signed certificate here.
    if (Test-Path "Cert:\CurrentUser\CA\$thumbprint") { Remove-Item "Cert:\CurrentUser\CA\$thumbprint" }
}

$names = @($HostName) + 'localhost' | Where-Object { $_ } | Select-Object -Unique
foreach ($ip in $IpAddress) {
    $parsed = $null
    if (-not [Net.IPAddress]::TryParse($ip, [ref]$parsed)) { throw "'$ip' is not a valid IP address." }
}

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$OutDir = (Resolve-Path $OutDir).Path
$rootPfxTarget = Join-Path $OutDir 'pharmacywarehouse-root.pfx'
$rootCer = Join-Path $OutDir 'pharmacywarehouse-root.cer'
$rootCrt = Join-Path $OutDir 'pharmacywarehouse-root.crt'
$serverPfx = Join-Path $OutDir 'pharmacywarehouse-server.pfx'

# A new root would force every device to install it again, so it is never replaced silently.
if (-not $RootPfx -and (Test-Path $rootPfxTarget)) {
    throw "$rootPfxTarget already exists. To renew the server certificate pass -RootPfx. To start a new CA use another -OutDir."
}
if ($RootPfx -and -not (Test-Path $RootPfx)) { throw "$RootPfx not found." }

if (-not $RootPassword) {
    if ($RootPfx) { $RootPassword = Read-Host -AsSecureString 'Password of the CA .pfx' }
    else { $RootPassword = Read-NewPassword 'New password for the CA .pfx' }
}
if (-not $ServerPassword) { $ServerPassword = Read-NewPassword 'Temporary password for the server .pfx' }
if (-not $RootPfx) { Assert-Password $RootPassword 'The CA password' }
Assert-Password $ServerPassword 'The server password'

$root = $null
$server = $null
try {
    if ($RootPfx) {
        Step "Loading the existing CA from $RootPfx"
        $root = Import-PfxCertificate -FilePath $RootPfx -Password $RootPassword -CertStoreLocation Cert:\CurrentUser\My -Exportable
        if ($root.NotAfter -lt (Get-Date).AddDays($ServerDays)) { Write-Warning "The CA expires on $($root.NotAfter); the server certificate cannot outlive it in practice." }
    }
    else {
        Step "Creating the CA (valid $RootYears years)"
        $root = New-SelfSignedCertificate -Type Custom -Subject 'CN=PharmacyWarehouse Local CA, O=PharmacyWarehouse' `
            -TextExtension @('2.5.29.19={critical}{text}CA=true&pathlength=0') `
            -KeyUsage CertSign, CRLSign, DigitalSignature `
            -KeyAlgorithm RSA -KeyLength 4096 -HashAlgorithm SHA256 -KeyExportPolicy Exportable `
            -NotAfter (Get-Date).AddYears($RootYears) `
            -CertStoreLocation Cert:\CurrentUser\My -FriendlyName 'PharmacyWarehouse Local CA'
        Export-PfxCertificate -Cert $root -FilePath $rootPfxTarget -Password $RootPassword | Out-Null
    }

    Export-Certificate -Cert $root -FilePath $rootCer -Force | Out-Null
    Copy-Item -Path $rootCer -Destination $rootCrt -Force

    Step "Creating the server certificate for $((@($names) + @($IpAddress)) -join ', ') (valid $ServerDays days)"
    # Browsers accept an IP only as an IPAddress entry of the Subject Alternative Name.
    $san = (@($names | ForEach-Object { "DNS=$_" }) + @($IpAddress | ForEach-Object { "IPAddress=$_" })) -join '&'
    $server = New-SelfSignedCertificate -Type Custom -Subject "CN=$(@($names)[0])" -Signer $root `
        -TextExtension @("2.5.29.17={text}$san", '2.5.29.37={text}1.3.6.1.5.5.7.3.1', '2.5.29.19={critical}{text}CA=false') `
        -KeyUsage DigitalSignature, KeyEncipherment `
        -KeyAlgorithm RSA -KeyLength 2048 -HashAlgorithm SHA256 -KeyExportPolicy Exportable `
        -NotAfter (Get-Date).AddDays($ServerDays) `
        -CertStoreLocation Cert:\CurrentUser\My -FriendlyName 'PharmacyWarehouse HTTPS'
    Export-PfxCertificate -Cert $server -FilePath $serverPfx -Password $ServerPassword -ChainOption EndEntityCertOnly -Force | Out-Null
}
finally {
    Remove-TemporaryCertificate $server
    Remove-TemporaryCertificate $root
}

Write-Host ''
Write-Host "Done. Files are in $OutDir" -ForegroundColor Green
Write-Host "Install on devices (public, safe to share): $rootCer or .crt"
Write-Host "Use on the server, then delete:             $serverPfx"
Write-Host "Keep off the server, never send to anyone:  $rootPfxTarget"
Write-Host "Renew the server certificate before:        $((Get-Date).AddDays($ServerDays).ToString('yyyy-MM-dd'))"
