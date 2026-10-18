$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$destination = Join-Path $repo 'artifacts/Serein-Gaming-WinUI'
$cacheLine = @(dotnet nuget locals global-packages --list) | Where-Object { $_ -match '^global-packages:' } | Select-Object -First 1
if (!$cacheLine) { throw 'Cannot locate NuGet license cache.' }
$cache = $cacheLine.Substring($cacheLine.IndexOf(':') + 1).Trim()
$assets = Get-Content "$PSScriptRoot/obj/project.assets.json" -Raw | ConvertFrom-Json
$dependencies = Get-Content "$destination/Serein.Gaming.WinUI.deps.json" -Raw | ConvertFrom-Json
$packages = @($assets.libraries.PSObject.Properties.Name | Where-Object { $_ -match '^Microsoft.WindowsAppSDK[^/]*/' })
$packages += @($dependencies.libraries.PSObject.Properties.Name | Where-Object { $_ -like 'runtimepack.*' } | ForEach-Object { $_.Substring('runtimepack.'.Length) })
$noticesDirectory = Join-Path $destination 'licenses'
New-Item -ItemType Directory -Force $noticesDirectory | Out-Null
foreach ($package in ($packages | Select-Object -Unique)) {
    $parts = $package.Split('/')
    $folder = Join-Path $cache ($parts[0].ToLowerInvariant() + '/' + $parts[1])
    $notices = @(Get-ChildItem -LiteralPath $folder -File | Where-Object { $_.Name -match '^(LICENSE|NOTICE|THIRD-PARTY-NOTICES)' })
    if (!$notices.Count) {
        $nuspec = Join-Path $folder ($parts[0].ToLowerInvariant() + '.nuspec')
        [xml]$metadata = Get-Content -LiteralPath $nuspec -Raw
        if (!$metadata.package.metadata.licenseUrl) { throw "Runtime license declaration missing: $package" }
        Copy-Item -LiteralPath $nuspec -Destination (Join-Path $noticesDirectory ($parts[0] + '-' + $parts[1] + '.nuspec')) -Force
    }
    foreach ($notice in $notices) { Copy-Item -LiteralPath $notice.FullName -Destination (Join-Path $noticesDirectory ($parts[0] + '-' + $parts[1] + '-' + $notice.Name)) -Force }
}
Get-ChildItem -LiteralPath $destination -File -Recurse | Where-Object Name -ne 'package-hashes.json' | ForEach-Object {
    [pscustomobject]@{ Name = $_.FullName.Substring($destination.TrimEnd('\', '/').Length + 1); SHA256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
} | ConvertTo-Json | Set-Content "$destination/package-hashes.json" -Encoding utf8
Write-Output "Runtime licenses and hashes: $destination"
