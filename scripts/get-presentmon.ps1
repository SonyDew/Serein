param([string]$Destination = (Join-Path $PSScriptRoot '../artifacts/tools/PresentMon-2.6.0-x64.exe'))
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSHOME 'Modules/Microsoft.PowerShell.Utility/Microsoft.PowerShell.Utility.psd1') -ErrorAction Stop
Import-Module (Join-Path $PSHOME 'Modules/Microsoft.PowerShell.Security/Microsoft.PowerShell.Security.psd1') -ErrorAction Stop
$path = [IO.Path]::GetFullPath($Destination)
$parent = [IO.Path]::GetDirectoryName($path)
New-Item -ItemType Directory -Path $parent -Force | Out-Null
if (Test-Path -LiteralPath $path) { throw 'Destination already exists; verify it without overwriting.' }
$url = 'https://github.com/GameTechDev/PresentMon/releases/download/v2.6.0/PresentMon-2.6.0-x64.exe'
$sha = 'B2A706BC6AD475749E3B7E3409263AA1E6906D45BDCF993F6DBC0F660188F1AF'
Invoke-WebRequest -Uri $url -OutFile $path
if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $sha) { throw 'PresentMon release checksum mismatch; do not execute downloaded file.' }
$signature = Get-AuthenticodeSignature -LiteralPath $path
if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'O=Intel Corporation') { throw 'PresentMon signature/publisher verification failed.' }
Write-Output "Verified standalone PresentMon 2.6.0: $path. Download only; no service or trace started."
