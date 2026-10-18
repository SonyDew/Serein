$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSHOME 'Modules/Microsoft.PowerShell.Utility/Microsoft.PowerShell.Utility.psd1') -ErrorAction Stop
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$package = Join-Path $repoRoot 'artifacts/Serein-win-x64'
$fixtureRoot = Join-Path $repoRoot ('artifacts/installer-fixture-' + [Guid]::NewGuid().ToString('N'))
& (Join-Path $package 'Install.ps1') -InstallRoot $fixtureRoot -Fixture
if (-not (Test-Path -LiteralPath (Join-Path $fixtureRoot 'Serein.exe'))) { throw 'Fixture install executable missing.' }
$marker = Get-Content -LiteralPath (Join-Path $fixtureRoot '.serein-install.json') -Raw | ConvertFrom-Json
foreach ($entry in $marker.Files) { if ((Get-FileHash -LiteralPath (Join-Path $fixtureRoot $entry.Name) -Algorithm SHA256).Hash -ne $entry.SHA256) { throw 'Installed fixture hash mismatch.' } }
$uninstall = Join-Path $fixtureRoot 'Uninstall.ps1'
$app = Join-Path $fixtureRoot 'Serein.exe'
if (Get-Process Serein -ErrorAction SilentlyContinue) { throw 'Close existing Serein before owned package launch test.' }
$fixtureData = Join-Path $repoRoot ('artifacts/package-ui-' + [Guid]::NewGuid().ToString('N'))
$fixtureApp = Start-Process -FilePath $app -WindowStyle Hidden -ArgumentList '--tray','--test-data',$fixtureData -PassThru
try {
    $ready = $false
    for ($attempt = 0; $attempt -lt 15 -and -not $ready; $attempt++) {
        if ($fixtureApp.HasExited) { throw 'Published WPF executable exited during startup.' }
        Start-Sleep -Milliseconds 500
        $status = Start-Process -FilePath $app -WindowStyle Hidden -ArgumentList '--status' -PassThru -Wait
        $ready = $status.ExitCode -eq 0
    }
    if (-not $ready) { throw 'Published WPF UI did not confirm loaded tray status.' }
    dotnet run --project (Join-Path $repoRoot 'tests/Serein.Tests') -c Release --no-build -- --probe-driver (Join-Path $fixtureRoot 'Serein.Admin.exe')
    if ($LASTEXITCODE -ne 0) { throw 'Packaged driver broker probe failed.' }
} finally {
    if (-not $fixtureApp.HasExited) {
        $exit = Start-Process -FilePath $app -WindowStyle Hidden -ArgumentList '--shutdown' -PassThru -Wait
        if ($exit.ExitCode -ne 0 -or -not $fixtureApp.WaitForExit(15000)) { throw 'Published WPF graceful shutdown failed.' }
    }
}
& $uninstall -Fixture
if (Test-Path -LiteralPath $fixtureRoot) { throw 'Fixture uninstall left files.' }
Write-Output 'PASS package hashes, fixture install, actual published WPF tray startup/status/graceful exit and complete fixture uninstall. No Start menu or host uninstall registration changed.'
