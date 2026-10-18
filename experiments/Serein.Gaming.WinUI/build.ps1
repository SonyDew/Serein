param([switch]$Test)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$destination = Join-Path $repo 'artifacts/Serein-Gaming-WinUI'
Push-Location $repo
try {
    dotnet publish "$PSScriptRoot/Serein.Gaming.WinUI.csproj" -c Release -p:Platform=x64 -o $destination
    if ($LASTEXITCODE -ne 0) { throw 'WinUI publish failed.' }
    foreach ($component in @('Serein.Host', 'Serein.Measure')) {
        $helperDestination = Join-Path $repo "artifacts/winui-helpers/$component"
        dotnet publish "src/$component/$component.csproj" -c Release -r win-x64 --self-contained true -o $helperDestination
        if ($LASTEXITCODE -ne 0) { throw "$component publish failed." }
        Copy-Item -Path "$helperDestination/$component.*" -Destination $destination -Force
    }
    Copy-Item "$PSScriptRoot/START_HERE.md" (Join-Path $destination 'README.md') -Force
    Copy-Item (Join-Path $repo 'LICENSE') $destination -Force
    Copy-Item "$PSScriptRoot/THIRD_PARTY.md" $destination -Force
    foreach ($file in @('App.xbf', 'MainWindow.xbf', 'Serein.Gaming.WinUI.pri', 'Serein.Gaming.WinUI.exe', 'Serein.Host.exe', 'Serein.Measure.exe')) {
        if (!(Test-Path -LiteralPath (Join-Path $destination $file))) { throw "Published file missing: $file" }
    }
    & "$PSScriptRoot/package.ps1"
    if ($Test) {
        dotnet build src/Serein.Host -c Release
        if ($LASTEXITCODE -ne 0) { throw 'Framework-dependent test host build failed.' }
        dotnet build tests/OptimizerFixture -c Release
        if ($LASTEXITCODE -ne 0) { throw 'Fixture build failed.' }
        dotnet run --project experiments/Serein.Gaming.WinUI.Tests -c Release
        if ($LASTEXITCODE -ne 0) { throw 'WinUI session tests failed.' }
        $uiResults = Join-Path $repo ('artifacts/winui-ui-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
        $ui = Start-Process -FilePath (Join-Path $destination 'Serein.Gaming.WinUI.exe') -ArgumentList @('--smoke', $uiResults) -PassThru
        try {
            $deadline = (Get-Date).AddSeconds(120)
            $resultFile = Join-Path $uiResults 'results.json'
            while (!(Test-Path -LiteralPath $resultFile) -and (Get-Date) -lt $deadline -and !$ui.HasExited) { Start-Sleep -Milliseconds 300; $ui.Refresh() }
            if (!(Test-Path -LiteralPath $resultFile)) { throw "WinUI smoke did not finish: $uiResults" }
            $result = Get-Content -LiteralPath $resultFile -Raw | ConvertFrom-Json
            if (!$result.Success) { throw "WinUI smoke failed: $($result.Error)" }
            Write-Output "PASS native UI: $uiResults"
        } finally {
            if (!$ui.HasExited) { $null = $ui.CloseMainWindow(); if (!$ui.WaitForExit(15000)) { throw 'Test UI did not close gracefully.' } }
            $ui.Dispose()
        }
    }
    Write-Output "Ready: $destination/Serein.Gaming.WinUI.exe"
} finally { Pop-Location }
