$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSHOME 'Modules/Microsoft.PowerShell.Utility/Microsoft.PowerShell.Utility.psd1') -ErrorAction Stop
Import-Module (Join-Path $PSHOME 'Modules/Microsoft.PowerShell.Archive/Microsoft.PowerShell.Archive.psd1') -ErrorAction Stop
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Push-Location $repoRoot
try {
    if (-not (Test-Path -LiteralPath 'artifacts/Serein-win-x64/Serein.exe')) { throw 'Publish the desktop, host and measurement projects first.' }
    Copy-Item -LiteralPath 'packaging/Install.ps1','packaging/Uninstall.ps1','README.md','packaging/THIRD_PARTY.md' -Destination 'artifacts/Serein-win-x64/' -Force
    Copy-Item -LiteralPath 'docs/DRIVER_SOURCES.md','docs/DRIVER_INSTALLATION.md','docs/KNOWN_LIMITATIONS.md','docs/DESIGN.md','docs/CPU_SENSORS.md' -Destination 'artifacts/Serein-win-x64/' -Force
    $packageReadme = Join-Path $repoRoot 'artifacts/Serein-win-x64/README.md'
    $readmeText = [IO.File]::ReadAllText($packageReadme).Replace('(docs/DRIVER_SOURCES.md)', '(DRIVER_SOURCES.md)').Replace('(docs/DRIVER_INSTALLATION.md)', '(DRIVER_INSTALLATION.md)').Replace('(docs/CPU_SENSORS.md)', '(CPU_SENSORS.md)')
    [IO.File]::WriteAllText($packageReadme, $readmeText, [Text.UTF8Encoding]::new($false))
    $nugetLines = @(dotnet nuget locals global-packages --list)
    if ($LASTEXITCODE -ne 0) { throw 'Cannot locate runtime package notices.' }
    $nugetLine = $nugetLines | Where-Object { $_ -match '^global-packages:' } | Select-Object -First 1
    if (-not $nugetLine) { throw 'Runtime package cache path missing.' }
    $nugetRoot = $nugetLine.Substring($nugetLine.IndexOf(':') + 1).Trim()
    $dependencies = Get-Content -LiteralPath 'artifacts/Serein-win-x64/Serein.deps.json' -Raw | ConvertFrom-Json
    foreach ($runtime in $dependencies.libraries.PSObject.Properties.Name | Where-Object { $_ -like 'runtimepack.*' }) {
        $parts = $runtime.Substring('runtimepack.'.Length).Split('/')
        $runtimeFolder = Join-Path $nugetRoot ($parts[0].ToLowerInvariant() + '/' + $parts[1])
        $notices = @(Get-ChildItem -LiteralPath $runtimeFolder -File | Where-Object { $_.Name -like 'LICENSE*' -or $_.Name -like 'THIRD-PARTY-NOTICES*' })
        if ($notices.Count -eq 0) { throw 'Runtime license missing.' }
        foreach ($notice in $notices) { Copy-Item -LiteralPath $notice.FullName -Destination (Join-Path 'artifacts/Serein-win-x64' ($parts[0] + '-' + $notice.Name + '.txt')) -Force }
    }
    Get-ChildItem -LiteralPath 'artifacts/Serein-win-x64' -File | Where-Object Name -ne 'package-hashes.json' | ForEach-Object { [PSCustomObject]@{ Name=$_.Name; SHA256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash } } | ConvertTo-Json | Set-Content -LiteralPath 'artifacts/Serein-win-x64/package-hashes.json' -Encoding UTF8
    Compress-Archive -Path 'artifacts/Serein-win-x64/*' -DestinationPath 'artifacts/Serein-win-x64.zip' -Force
    Write-Output 'Packaged existing published binaries with README, original runtime licenses/notices and hashes.'
} finally { Pop-Location }
