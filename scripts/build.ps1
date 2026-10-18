param([switch]$Publish, [switch]$Test)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSHOME 'Modules/Microsoft.PowerShell.Utility/Microsoft.PowerShell.Utility.psd1') -ErrorAction Stop
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Push-Location $repoRoot
try {
    dotnet build Serein.sln -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    Copy-Item -Path 'src/Serein.Host/bin/Release/net10.0-windows/Serein.Host.*' -Destination 'src/Serein.Desktop/bin/Release/net10.0-windows/' -Force
    Copy-Item -Path 'src/Serein.Admin/bin/Release/net10.0-windows/Serein.Admin.*' -Destination 'src/Serein.Desktop/bin/Release/net10.0-windows/' -Force
    Copy-Item -Path 'src/Serein.Measure/bin/Release/net10.0-windows/Serein.Measure.*' -Destination 'src/Serein.Desktop/bin/Release/net10.0-windows/' -Force
    if ($Test) {
        dotnet run --project tests/Serein.Tests -c Release --no-build
        if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
    }
    if ($Publish) {
        dotnet publish src/Serein.Desktop -c Release -r win-x64 --self-contained true -o artifacts/Serein-win-x64
        if ($LASTEXITCODE -ne 0) { throw 'Desktop publish failed.' }
        dotnet publish src/Serein.Host -c Release -r win-x64 --self-contained true -o artifacts/host-win-x64
        if ($LASTEXITCODE -ne 0) { throw 'Host publish failed.' }
        dotnet publish src/Serein.Measure -c Release -r win-x64 --self-contained true -o artifacts/measure-win-x64
        if ($LASTEXITCODE -ne 0) { throw 'Measurement console publish failed.' }
        dotnet publish src/Serein.Admin -c Release -r win-x64 --self-contained true -o artifacts/admin-win-x64
        if ($LASTEXITCODE -ne 0) { throw 'Driver broker publish failed.' }
        # Console publish contains WindowsBase/System.Drawing facade assemblies. Never overwrite WPF runtime files.
        Copy-Item -Path 'artifacts/host-win-x64/Serein.Host.*' -Destination 'artifacts/Serein-win-x64/' -Force
        Copy-Item -Path 'artifacts/measure-win-x64/Serein.Measure.*' -Destination 'artifacts/Serein-win-x64/' -Force
        Copy-Item -Path 'artifacts/admin-win-x64/Serein.Admin.*' -Destination 'artifacts/Serein-win-x64/' -Force
        & (Join-Path $PSScriptRoot 'package.ps1')
    }
} finally { Pop-Location }
